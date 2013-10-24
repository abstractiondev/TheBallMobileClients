using System;
using System.Collections.Generic;
using System.IO;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;
using SecuritySupport;
using WP8LocationUpdateClient.Resources;
using Windows.Devices.Geolocation;

namespace WP8LocationUpdateClient
{
    public partial class MainPage : PhoneApplicationPage
    {
        // Constructor
        public MainPage()
        {
            InitializeComponent();

            // Sample code to localize the ApplicationBar
            //BuildLocalizedApplicationBar();
        }

        // Sample code for building a localized ApplicationBar
        //private void BuildLocalizedApplicationBar()
        //{
        //    // Set the page's ApplicationBar to a new instance of ApplicationBar.
        //    ApplicationBar = new ApplicationBar();

        //    // Create a new button and set the text value to the localized string from AppResources.
        //    ApplicationBarIconButton appBarButton = new ApplicationBarIconButton(new Uri("/Assets/AppBar/appbar.add.rest.png", UriKind.Relative));
        //    appBarButton.Text = AppResources.AppBarButtonText;
        //    ApplicationBar.Buttons.Add(appBarButton);

        //    // Create a new menu item with the localized string from AppResources.
        //    ApplicationBarMenuItem appBarMenuItem = new ApplicationBarMenuItem(AppResources.AppBarMenuItemText);
        //    ApplicationBar.MenuItems.Add(appBarMenuItem);
        //}
        protected override void OnNavigatedTo(System.Windows.Navigation.NavigationEventArgs e)
        {
            if (IsolatedStorageSettings.ApplicationSettings.Contains("LocationConsent"))
            {
                // User has opted in or out of Location
                return;
            }
            else
            {
                MessageBoxResult result =
                    MessageBox.Show("This app accesses your phone's location. Is that ok?",
                    "Location",
                    MessageBoxButton.OKCancel);

                if (result == MessageBoxResult.OK)
                {
                    IsolatedStorageSettings.ApplicationSettings["LocationConsent"] = true;
                }
                else
                {
                    IsolatedStorageSettings.ApplicationSettings["LocationConsent"] = false;
                }

                IsolatedStorageSettings.ApplicationSettings.Save();
            }
        }

        private async void OneShotLocation_Click(object sender, RoutedEventArgs e)
        {

            if ((bool)IsolatedStorageSettings.ApplicationSettings["LocationConsent"] != true)
            {
                // The user has opted out of Location.
                return;
            }

            Geolocator geolocator = new Geolocator();
            geolocator.DesiredAccuracyInMeters = 50;

            try
            {
                Geoposition geoposition = await geolocator.GetGeopositionAsync(
                    maximumAge: TimeSpan.FromMinutes(5),
                    timeout: TimeSpan.FromSeconds(10)
                    );

                LatitudeTextBlock.Text = geoposition.Coordinate.Latitude.ToString("0.00");
                LongitudeTextBlock.Text = geoposition.Coordinate.Longitude.ToString("0.00");
            }
            catch (Exception ex)
            {
                if ((uint)ex.HResult == 0x80004004)
                {
                    // the application does not have the right capability or the location master switch is off
                    StatusTextBlock.Text = "location  is disabled in phone settings.";
                }
                //else
                {
                    // something else happened acquring the location
                }
            }
        }


        private void WSNegotiation_Click(object sender, RoutedEventArgs e)
        {
            string hostName = tHostName.Text;
            if (String.IsNullOrEmpty(hostName))
            {
                MessageBox.Show("Please enter the Ball hostname");
                return;
            }
            string groupID = tGroupID.Text;
            //if (groupID == "")
            //    groupID = "fd17836d-30e4-4d96-ae95-9217ac48867d";
            if (String.IsNullOrEmpty(groupID))
            {
                MessageBox.Show("Please enter the Group ID");
                return;
            }
            string deviceDescription = tDeviceDescription.Text;
            if (String.IsNullOrEmpty(deviceDescription))
            {
                MessageBox.Show("Please enter the device description");
                return;
            }

            string connectionUrl = String.Format("ws://{0}/websocket/NegotiateDeviceConnection?groupID={1}", hostName, groupID);
                try
                {
                    var result = SecurityNegotiationManager.PerformEKEInitiatorAsBob(connectionUrl, "testsecretXYZ33",
                                                                                     deviceDescription);
                    StoreNegotiatedCredentials(result, hostName, groupID);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK);
                }
        }

        private void StoreNegotiatedCredentials(SecurityNegotiationResult result, string hostName, string groupId)
        {
            IsolatedStorageSettings.ApplicationSettings["AESKey"] = result.AESKey;
            IsolatedStorageSettings.ApplicationSettings["EstablishedTrustID"] = result.EstablishedTrustID;
            IsolatedStorageSettings.ApplicationSettings["HostName"] = hostName;
            IsolatedStorageSettings.ApplicationSettings["GroupID"] = groupId;
            IsolatedStorageSettings.ApplicationSettings.Save();
        }

        private void PushContentToGroup_Click(object sender, RoutedEventArgs e)
        {
            string hostName = (string) IsolatedStorageSettings.ApplicationSettings["HostName"];
            string groupID = (string) IsolatedStorageSettings.ApplicationSettings["GroupID"];
            byte[] aesKey = (byte[]) IsolatedStorageSettings.ApplicationSettings["AESKey"];
            string establishedTrustID = (string) IsolatedStorageSettings.ApplicationSettings["EstablishedTrustID"];
            string destinationUrl = string.Format("https://{0}/auth/grp/{1}", hostName, groupID);
            string location = LatitudeTextBlock.Text + "," + LongitudeTextBlock.Text;
            string locationContentName = "mylocation.txt";

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(destinationUrl);
            request.Method = "POST";
            AesManaged aes = new AesManaged();
            aes.KeySize = 256;
            aes.GenerateIV();
            aes.Key = aesKey;
            var ivBase64 = Convert.ToBase64String(aes.IV);
            request.Headers["Authorization"] = "DeviceAES:" + ivBase64 + ":" + establishedTrustID + ":" + locationContentName;
            request.BeginGetRequestStream(result =>
                {
                    HttpWebRequest req = (HttpWebRequest) result.AsyncState;
                    var requestStream = req.EndGetRequestStream(result);

                    var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
                    var cryptoStream = new CryptoStream(requestStream, encryptor, CryptoStreamMode.Write);
                    StreamWriter writer = new StreamWriter(cryptoStream);
                    writer.WriteLine(location);
                    writer.Flush();
                    cryptoStream.Close();
                    req.BeginGetResponse(respResult =>
                        {

                            var respReq = (HttpWebRequest) respResult.AsyncState;
                            var response = (HttpWebResponse) respReq.EndGetResponse(respResult);
                            if (response.StatusCode != HttpStatusCode.OK)
                                throw new InvalidOperationException(
                                    "PushToInformationOutput failed with Http status: " +
                                    response.StatusCode.ToString());
                        }, req);
                }, request);

        }
    }
}