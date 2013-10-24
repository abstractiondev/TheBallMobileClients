using System;
using System.Diagnostics;
using System.IO;
//using System.Net.WebSockets;
using System.Text;
using System.Threading;
using SuperSocket.ClientEngine;
using WebSocket4Net;

namespace SecuritySupport
{
    public class SecurityNegotiationResult
    {
        public byte[] AESKey;
        public string EstablishedTrustID;
    }

    public class SecurityNegotiationManager
    {
        //public static async Task EchoClient()
        private WebSocket Socket;
        private INegotiationProtocolMember ProtocolMember;
        private string DeviceDescription;
        Stopwatch watch = new Stopwatch();
        private bool PlayAsAlice = false;
        private SemaphoreSlim WaitingSemaphore = new SemaphoreSlim(0);
        private TimeSpan MAX_NEGOTIATION_TIME = new TimeSpan(0, 0, 0, 10);
        private string EstablishedTrustID;

        public static SecurityNegotiationResult PerformEKEInitiatorAsAlice(string connectionUrl, string sharedSecret, string deviceDescription)
        {
            return performEkeInitiator(connectionUrl, sharedSecret, deviceDescription, true);
        }

        public static SecurityNegotiationResult PerformEKEInitiatorAsBob(string connectionUrl, string sharedSecret, string deviceDescription)
        {
            return performEkeInitiator(connectionUrl, sharedSecret, deviceDescription, false);
        }

        private static SecurityNegotiationResult performEkeInitiator(string connectionUrl, string sharedSecret, string deviceDescription,
                                                                     bool playAsAlice)
        {
            var securityNegotiationManager = InitSecurityNegotiationManager(connectionUrl, sharedSecret, deviceDescription, playAsAlice);
            // Perform negotiation
            securityNegotiationManager.PerformNegotiation();
            var aesKey = securityNegotiationManager.ProtocolMember.NegotiationResults[0];
            string deviceID = securityNegotiationManager.EstablishedTrustID;
            return new SecurityNegotiationResult {AESKey = aesKey, EstablishedTrustID = deviceID};
        }


        private void PerformNegotiation()
        {
            watch.Start();
            Socket.Open();
            bool negotiationSuccess = WaitingSemaphore.Wait(MAX_NEGOTIATION_TIME);
            if(!negotiationSuccess)
                throw new TimeoutException("Trust negotiation timed out");
            Socket.Close();
        }

        public static void EchoClient()
        {
            Console.WriteLine("Starting EKE WSS connection");
            //string hostWithProtocolAndPort = "ws://192.168.0.12:50430";
            //string hostWithProtocolAndPort = "ws://localhost:50430";
            //string hostWithProtocolAndPort = "ws://169.254.80.80:50430";
            
            string hostWithProtocolAndPort = "ws://test.caloom.com";
            //string idParam = "accountemail=kalle.launiala@gmail.com";
            string idParam = "groupID=ecc5fac6-49d3-4c57-b01b-349d83503d93";
            string deviceConnectionUrl = hostWithProtocolAndPort + "/websocket/NegotiateDeviceConnection" + "?" + idParam;
            //socket = new WebSocket("wss://theball.protonit.net/websocket/mytest.k");
            string sharedSecret = "testsecretXYZ33";
            var securityNegotiationManager = InitSecurityNegotiationManager(deviceConnectionUrl, sharedSecret, "test device desc", false);
            //var securityNegotiationManager = InitSecurityNegotiationManager(hostWithProtocolAndPort, sharedSecret, "test device desc", false);
            securityNegotiationManager.PerformNegotiation();
#if native45

    //WebSocket socket = new ClientWebSocket();
    //WebSocket.CreateClientWebSocket()
            ClientWebSocket socket = new ClientWebSocket();
            Uri uri = new Uri("ws://localhost:50430/websocket/mytest.k");
            var cts = new CancellationTokenSource();
            await socket.ConnectAsync(uri, cts.Token);

            Console.WriteLine(socket.State);

            Task.Factory.StartNew(
                async () =>
                {
                    var rcvBytes = new byte[128];
                    var rcvBuffer = new ArraySegment<byte>(rcvBytes);
                    while (true)
                    {
                        WebSocketReceiveResult rcvResult = await socket.ReceiveAsync(rcvBuffer, cts.Token);
                        byte[] msgBytes = rcvBuffer.Skip(rcvBuffer.Offset).Take(rcvResult.Count).ToArray();
                        string rcvMsg = Encoding.UTF8.GetString(msgBytes);
                        Console.WriteLine("Received: {0}", rcvMsg);
                    }
                }, cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            while (true)
            {
                var message = Console.ReadLine();
                if (message == "Bye")
                {
                    cts.Cancel();
                    return;
                }
                byte[] sendBytes = Encoding.UTF8.GetBytes(message);
                var sendBuffer = new ArraySegment<byte>(sendBytes);
                await
                    socket.SendAsync(sendBuffer, WebSocketMessageType.Text, endOfMessage: true,
                                     cancellationToken: cts.Token);
            }

#endif
        }

        private static SecurityNegotiationManager InitSecurityNegotiationManager(string deviceConnectionUrl, string sharedSecret, string deviceDescription, bool playAsAlice)
        {
            SecurityNegotiationManager securityNegotiationManager = new SecurityNegotiationManager();
            securityNegotiationManager.Socket = new WebSocket(deviceConnectionUrl);
            securityNegotiationManager.Socket.Opened += securityNegotiationManager.socket_OnOpen;
            securityNegotiationManager.Socket.Closed += securityNegotiationManager.socket_OnClose;
            securityNegotiationManager.Socket.Error += securityNegotiationManager.socket_OnError;
            securityNegotiationManager.Socket.DataReceived += securityNegotiationManager.socket_OnData;
            securityNegotiationManager.Socket.MessageReceived += securityNegotiationManager.socket_OnMessage;
            TheBallEKE instance = new TheBallEKE();
            instance.InitiateCurrentSymmetricFromSecret(sharedSecret);
            securityNegotiationManager.PlayAsAlice = playAsAlice;
            securityNegotiationManager.DeviceDescription = deviceDescription;
            if (securityNegotiationManager.PlayAsAlice)
            {
                securityNegotiationManager.ProtocolMember = new TheBallEKE.EKEAlice(instance);
            }
            else
            {
                securityNegotiationManager.ProtocolMember = new TheBallEKE.EKEBob(instance);
            }
            securityNegotiationManager.ProtocolMember.SendMessageToOtherParty =
                bytes => securityNegotiationManager.Socket.Send(bytes, 0, bytes.Length);
            return securityNegotiationManager;
        }

        private void socket_OnData(object sender, DataReceivedEventArgs dataReceivedEventArgs)
        {
            var rawData = dataReceivedEventArgs.Data;
            Debug.WriteLine("Received message: " + rawData.Length.ToString());
            if (!ProtocolMember.IsDoneWithProtocol)
            {
                ProtocolMember.LatestMessageFromOtherParty = rawData;
                ProceedProtocol();
            }
        }

        void socket_OnMessage(object sender, MessageReceivedEventArgs messageReceivedEventArgs)
        {
            if (ProtocolMember.IsDoneWithProtocol)
            {
                string strData = messageReceivedEventArgs.Message;
                if(String.IsNullOrEmpty(strData))
                    throw new InvalidDataException("Negotiation protocol end requires EstablishedTrustID as text");
                EstablishedTrustID = strData;
                watch.Stop();
                WaitingSemaphore.Release();
            }
        }

        void socket_OnError(object sender, ErrorEventArgs errorEventArgs)
        {
            var errorMessage = errorEventArgs.Exception.ToString();
            Debug.WriteLine("ERROR: " + errorMessage);
        }

        void socket_OnClose(object sender, EventArgs eventArgs)
        {
            Debug.WriteLine("Closed");
        }

        void socket_OnOpen(object sender, EventArgs e)
        {
            Debug.WriteLine("Opened");
            if (PlayAsAlice)
                ProceedProtocol();
            else
                PingAlice();
        }

        private void PingAlice()
        {
            Socket.Send(new byte[0], 0, 0);
        }

        void ProceedProtocol()
        {
            while(ProtocolMember.IsDoneWithProtocol == false && ProtocolMember.WaitForOtherParty == false)
            {
                ProtocolMember.PerformNextAction();
            } 
            if (ProtocolMember.IsDoneWithProtocol)
            {
                Socket.Send(DeviceDescription); 
                Debug.WriteLine((PlayAsAlice ? "Alice" : "Bob") + " done with EKE in " + watch.ElapsedMilliseconds.ToString() + " ms!");
            }
        }

    }
}