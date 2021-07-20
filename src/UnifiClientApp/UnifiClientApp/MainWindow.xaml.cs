﻿using dotMorten.Unifi.Protect.DataModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Notifications;

namespace UnifiClientApp
{
    public sealed partial class MainWindow : Window
    {
        private dotMorten.Unifi.ProtectClient protectClient;

        public MainWindow()
        {
            this.InitializeComponent();
            this.Title = "Unifi Protect";
            if(Windows.Storage.ApplicationData.Current.LocalSettings.Containers.ContainsKey("credentials"))
            {
                var container = Windows.Storage.ApplicationData.Current.LocalSettings.Containers["credentials"].Values;
                if (container.ContainsKey("hostname") && container.ContainsKey("username") && container.ContainsKey("password"))
                {
                    tbHostname.Text = (string)container["hostname"];
                    tbUsername.Text = (string)container["username"];
                    pwdBox.Password = (string)container["password"];
                    AutoSignin();
                }
            }
        }

        private async void AutoSignin()
        {
            signinArea.Visibility = Visibility.Collapsed;
            var container = Windows.Storage.ApplicationData.Current.LocalSettings.CreateContainer("credentials", Windows.Storage.ApplicationDataCreateDisposition.Always);
            try
            {
                var client = new dotMorten.Unifi.ProtectClient((string)container.Values["hostname"], (string)container.Values["username"], (string)container.Values["password"], (bool)container.Values["ignoreSsl"]);
                await client.OpenAsync(CancellationToken.None);
                InitClient(client);
            }
            catch
            {
                signinArea.Visibility = Visibility.Visible;
            }
        }

        private async void SignIn_Click(object sender, RoutedEventArgs e)
        {
            Button button = (Button)sender;

            try
            {
                button.IsEnabled = false;
                progress.IsActive = true;
                var protectClient = new dotMorten.Unifi.ProtectClient(tbHostname.Text, tbUsername.Text, pwdBox.Password, true);
                await protectClient.OpenAsync(CancellationToken.None);
                var container = Windows.Storage.ApplicationData.Current.LocalSettings.CreateContainer("credentials", Windows.Storage.ApplicationDataCreateDisposition.Always);
                container.Values["hostname"] = protectClient.HostName;
                container.Values["ignoreSsl"] = protectClient.IgnoreSslErrors;
                container.Values["username"] = tbUsername.Text;
                container.Values["password"] = pwdBox.Password;
                signinArea.Visibility = Visibility.Collapsed;
                InitClient(protectClient);
            }
            catch(Exception ex)
            {
                ContentDialog cd = new ContentDialog()
                {
                    Content = new TextBlock() { Text = ex.Message, TextWrapping = TextWrapping.Wrap },
                    CloseButtonText = "OK",
                    Title = "Error signing in",
                    XamlRoot = LayoutRoot.XamlRoot
                };
                await cd.ShowAsync();
            }
            finally
            {
                button.IsEnabled = true;
                progress.IsActive = true;
            }
        }

        private void InitClient(dotMorten.Unifi.ProtectClient client)
        {
            if(protectClient != null)
            {
                protectClient.Ring -= ProtectClient_Ring;
                protectClient.Motion -= ProtectClient_Motion;
                protectClient.SmartDetectZone -= ProtectClient_SmartDetectZone;
                protectClient.Disconnected -= ProtectClient_Disconnected;
                protectClient = null;
            }
            if (client != null)
            {
                protectClient = client;
                protectClient.Ring += ProtectClient_Ring;
                protectClient.Motion += ProtectClient_Motion;
                protectClient.SmartDetectZone += ProtectClient_SmartDetectZone;
                protectClient.Disconnected += ProtectClient_Disconnected;
                status.Text = $"Connected to '{client.HostName}'\nFound {client.System.Cameras.Count} cameras:\n" +
                    string.Join("", client.System.Cameras.Select(c => $" - {c.Name} ({c.Type}){ (c.IsConnected ? "" : " (disconnected)") }\n"));
            }
        }

        private void ProtectClient_Disconnected(object sender, EventArgs e)
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
            {
                status.Text += "Disconnected\n";
            });
        }

        private void ProtectClient_SmartDetectZone(object sender, dotMorten.Unifi.CameraEventArgs e)
        {
            var type = e.SmartDetectTypes[0][0].ToString().ToUpper() + e.SmartDetectTypes[0].Substring(1);
            Debug.WriteLine($"{type} detected on camera '{e.Camera.Name}'");
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
            {
                status.Text += $"{DateTimeOffset.Now} {type} detected on camera '{e.Camera.Name}'\n";
                ShowCamera(e.Camera, e.Camera.Name, $"{type} detected", e.Id, type);
            });
        }

        private void ProtectClient_Motion(object sender, dotMorten.Unifi.CameraEventArgs e)
        {
            Debug.WriteLine($"Motion detected on camera '{e.Camera.Name}'");
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
            {
                status.Text += $"{DateTimeOffset.Now} Motion detected on camera '{e.Camera.Name}'\n";
                ShowCamera(e.Camera, e.Camera.Name, $"Motion detected", e.Id, "Motion");
            });
        }

        private void ProtectClient_Ring(object sender, dotMorten.Unifi.CameraEventArgs e)
        {
            Debug.WriteLine($"Ring detected on camera '{e.Camera.Name}'");
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
            {
                status.Text += $"{DateTimeOffset.Now} Ring detected on camera '{e.Camera.Name}'\n";
                ShowCamera(e.Camera, e.Camera.Name, $"Ring detected", e.Id, "Ring");
            });
        }

        private async void ShowCamera(Camera camera, string title, string subtitle, string eventId, string resourceId)
        {
            notificationPopup.IconSource = LayoutRoot.Resources.ContainsKey(resourceId) ? LayoutRoot.Resources[resourceId] as IconSource : null;
            using var c = await protectClient.GetCameraSnapshot(camera, false);
            BitmapImage img = new BitmapImage();
            var ms = new MemoryStream();
            c.CopyTo(ms);
            ms.Seek(0, SeekOrigin.Begin);
            img.SetSource(ms.AsRandomAccessStream());
            notificationImageSource.Source = img;
            notificationPopup.Tag = eventId;
            notificationPopup.Title = title;
            notificationPopup.Subtitle = subtitle;
            notificationPopup.IsOpen = true;


            var p = Path.GetTempFileName();
            using (var f = File.OpenWrite(p))
            {
                ms.Seek(0, SeekOrigin.Begin);
                ms.CopyTo(f);
            }

            string toastXmlString = @"<toast><visual><binding template=""ToastGeneric""><text hint-maxLines=""1"" /><text /><image /></binding></visual></toast>";
            var toastXml = new Windows.Data.Xml.Dom.XmlDocument();
            toastXml.LoadXml(toastXmlString);
            var stringElements = toastXml.GetElementsByTagName("text");
            stringElements[0].AppendChild(toastXml.CreateTextNode(title));
            stringElements[1].AppendChild(toastXml.CreateTextNode(subtitle));
            String imagePath = "file:///" + p.Replace('\\', '/');
            var imageElements = toastXml.GetElementsByTagName("image");
            ((Windows.Data.Xml.Dom.XmlElement)imageElements[0]).SetAttribute("src", imagePath);
            ToastNotification toast = new ToastNotification(toastXml);
            ToastNotificationManager.GetDefault().CreateToastNotifier().Show(toast);
            

            await Task.Delay(5000);
            if (notificationPopup.Tag as string == eventId)
                notificationPopup.IsOpen = false;
            File.Delete(p);


        }
    }
}
