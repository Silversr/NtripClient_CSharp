using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Net.Sockets;
using System.Net;

namespace Ntrip
{
    public interface IClient
    {
        Action<byte[]> dataUpdated { get; set; }

        void readData();
        void ConfigClient(string userName, string password, string caster, string port, string mountpoint);
    }

    public class NtripClient:IClient
    {
        //-------------static fields--------------------
        public static double version = 0.2;
        public static string useragent = $"NTRIP JCMBsoftPythonClient/{version}";

        //-------------Delegate---------------------------------
        public Action<byte[]> dataUpdated { get; set; } = null;

        //-------------Client Constant Parameter-------------------------------------
        private const int factor = 2;// # How much the sleep time increases with each failed attempt
        private static int maxReconnect = 1;
        private const int maxReconnectTime = 1200;
        private const int sleepTime = 1;// # So the first one is 1 second
        private static int maxConnectTime = 0;

        //-------------Fields-----------------
        private byte[] buffer;// corresponding to Python NtripClient buffer = 50; size not sure
        private string user; //base64String
        private int port;
        private string caster;
        private string mountpoint;
        private float lat = 46;
        private float lon = 122;
        private float height = 1212;
        private bool verbose;
        private bool ssl;
        private bool host;
        private int UDP_Port;
        private bool V2;
        private Action<Object> headerFile = null;
        private bool headerOutput;
        private int maxConnectionTime;

        private Socket socket { get; set; } = null;

        private Direction _NorS;
        private Direction NorS
        {
            get { return _NorS; }
            set { if (value != Direction.East && value != Direction.West) { _NorS = value; } }
        }
        private Direction _EorW;
        private Direction EorW
        {
            get { return _EorW; }
            set { if (value != Direction.North && value != Direction.South) { _EorW = value; } }
        }

        private int lonDeg, latDeg;
        private float lonMin, latMin;
        //-------------Tyep Define------------------------------
        public enum Direction { South, North, East, West }

        //-------------NtripClient Parameter Argument List---------------
        public static class NtripArgs
        {
            /*
            parser.add_option("-u", "--user", type="string", dest="user", default="IBS", help="The Ntripcaster username.  Default: %default")
            parser.add_option("-p", "--password", type="string", dest="password", default="IBS", help="The Ntripcaster password. Default: %default")
            parser.add_option("-o", "--org", type="string", dest="org", help="Use IBSS and the provided organization for the user. Caster and Port are not needed in this case Default: %default")
            parser.add_option("-b", "--baseorg", type="string", dest="baseorg", help="The org that the base is in. IBSS Only, assumed to be the user org")
            parser.add_option("-t", "--latitude", type="float", dest="lat", default=50.09, help="Your latitude.  Default: %default")
            parser.add_option("-g", "--longitude", type="float", dest="lon", default=8.66, help="Your longitude.  Default: %default")
            parser.add_option("-e", "--height", type="float", dest="height", default=1200, help="Your ellipsoid height.  Default: %default")
            parser.add_option("-v", "--verbose", action="store_true", dest="verbose", default=False, help="Verbose")
            parser.add_option("-s", "--ssl", action="store_true", dest="ssl", default=False, help="Use SSL for the connection")
            parser.add_option("-H", "--host", action="store_true", dest="host", default=False, help="Include host header, should be on for IBSS")
            parser.add_option("-r", "--Reconnect", type="int", dest="maxReconnect", default=1, help="Number of reconnections")
            parser.add_option("-D", "--UDP", type="int", dest="UDP", default=None, help="Broadcast recieved data on the provided port")
            parser.add_option("-2", "--V2", action="store_true", dest="V2", default=False, help="Make a NTRIP V2 Connection")
            parser.add_option("-f", "--outputFile", type="string", dest="outputFile", default=None, help="Write to this file, instead of stdout")
            parser.add_option("-m", "--maxtime", type="int", dest="maxConnectTime", default=None, help="Maximum length of the connection, in seconds")

            parser.add_option("--Header", action="store_true", dest="headerOutput", default=False, help="Write headers to stderr")
    ````````parse.add_option("--HeaderFile", type="string", dest="headerFile", default=None, help="Write headers to this file, instead of stderr.")
             */
            static public string user { get; set; } = "IBS";
            static public string password { get; set; } = "IBS";
            static public string caster { get; set; }
            static public int port { get; set; }
            static public string mountpoint { get; set; }
            static public string org { get; set; } = "";
            static string baseorg { get; set; } = "";
            static public float lat { get; set; } = 50.09f;
            static public float lon { get; set; } = 8.66f;
            static public float height { get; set; } = 1200f;
            static public bool verbose { get; set; } = false;
            static public bool ssl { get; set; } = false;
            static public bool host { get; set; } = false;
            static public int maxReconnect { get; set; } = 1;
            static public int UDP_Port { get; set; }
            static public bool V2 { get; set; } = false;
            static public string outputFile { get; set; }
            static public int maxConnectionTIme { get; set; }

            static public bool headerOutput { get; set; } = false;
            static public string headerFile { get; set; }
        }

        //---------------Constructor--------------------------------
        public NtripClient()
        {
            this.ConfigNtripClientDefault();
        }

        //---------------Default Config for NtripClient--------------------------------
        private void ConfigNtripClientDefault()
        {
            this.buffer = new byte[100];
            this.user = NtripArgs.user.ToBase64String();
            this.port = NtripArgs.port;
            this.caster = NtripArgs.caster;
            this.mountpoint = NtripArgs.mountpoint;
            this.setPosition(lat, lon);
            this.height = NtripArgs.height;
            this.verbose = NtripArgs.verbose;
            this.ssl = NtripArgs.ssl;
            this.host = NtripArgs.host;
            this.UDP_Port = NtripArgs.UDP_Port;
            this.V2 = NtripArgs.V2;
            this.headerFile += Console.Write; // could be updated 
            this.headerOutput = NtripArgs.headerOutput;
            this.maxConnectionTime = NtripArgs.maxConnectionTIme;

            this.NorS = Direction.North;
            this.EorW = Direction.East;
        }

        //---------------Config for NtripClient-------------------------------------------
        public void ConfigClient(string userName, string password, string caster, string port, string mountpoint)
        {
            //Config all arguments
            //NtripArgs.ssl = false;
            NtripArgs.user = $"{userName}:{password}";
            NtripArgs.caster = caster; Console.WriteLine($"caster IP is {NtripArgs.caster}");
            NtripArgs.port = int.Parse(port); Console.WriteLine($"int.Parse(port) is {NtripArgs.port}");
            NtripArgs.mountpoint = $"/{mountpoint}"; Console.WriteLine($"mountpoint is {NtripArgs.mountpoint}");

            this.user = NtripArgs.user.ToBase64String(); ;
            this.caster = NtripArgs.caster;
            this.port = NtripArgs.port;
            this.mountpoint = NtripArgs.mountpoint;
        }

        //---------------Read data from server: might be async need modification-----------------------
        public void readData()
        {
            int reconnectTry = 1;
            int sleepTime = 1;
            int reconnectTime = 0;

            //------------------------Socket Part----------------------------
            // Data buffer for incoming data.
            //this.buffer;
            // Connect to a remote device
            try
            {
                // Establish the remote endpoint for the socket. Two method;
                //Console.WriteLine(this.caster);
                IPHostEntry ipHostInfo = Dns.GetHostEntry(IPAddress.Parse(this.caster));
                //IPEndPoint remoteEP = new IPEndPoint(IPAddress.Parse(this.caster), this.port);
                IPEndPoint remoteEP = new IPEndPoint(ipHostInfo.AddressList[0], this.port);

                //IP Info check Console.WriteLine();
                {
                    /*
                        //IPEndPoint remoteEP = new IPEndPoint(IPAddress.Parse(this.caster), this.port);
                        //Console.WriteLine(IPAddress.Parse(this.caster).ToString());
                        //Console.WriteLine(ipHostInfo.AddressList.Count());
                        //foreach (var ip in ipHostInfo.AddressList)
                        //{

                        //    Console.WriteLine(ip.ToString());
                        //}
                        //Console.WriteLine(remoteEP.ToString());
                        //Console.WriteLine($"IPHostEntry instance's hostName is {ipHostInfo.HostName}");
                    */
                }

                // Create a Tcp/Ip socket
                this.socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                // Connect the socket to the remote endpoint. Catch any errors.
                try
                {
                    //Connect to server
                    this.socket.Connect(remoteEP);

                    //set time out
                    this.socket.SendTimeout = 20000;
                    this.socket.ReceiveTimeout = 20000;



                    //check server is OK or not
                    bool ICY200OK = false;
                    while (!ICY200OK)
                    {
                        // Encode the data string into a byte array. for send
                        byte[] checkServer = Encoding.ASCII.GetBytes(this.getMountPointString());

                        //send the request i guess
                        this.socket.Send(checkServer);

                        // Receive the response from the remote device.
                        //int bytesRec = socket.Receive(this.buffer);
                        int bytesRec = socket.Receive(this.buffer, 50, SocketFlags.None);

                        Console.WriteLine($"Client Received number of bytes: {bytesRec}");
                        Console.WriteLine($"Client Received info: {Encoding.ASCII.GetString(buffer, 0, bytesRec)}");

                        Thread.Sleep(200);
                        if (Encoding.ASCII.GetString(buffer, 0, bytesRec).Contains("ICY 200 OK"))
                        {
                            ICY200OK = true;
                        }

                    }
                    //---------------start to get data from server------------------------
                    // Encode the data string into a byte array. for send
                    byte[] ggaString = Encoding.ASCII.GetBytes(this.getGGAString());

                    //send request to get data from server
                    this.socket.Send(ggaString);

                    while (this.buffer.Count() > 0)
                    {
                        // Receive the response from the remote device
                        int bytesDataRec = this.socket.Receive(this.buffer);
                        Console.WriteLine($"Client Received number of bytes: {bytesDataRec}");
                        this.dataUpdated(this.buffer);
                        //int bytesDataRec = this.socket.Receive(this.buffer, 50, SocketFlags.None);

                        //delegate for message transmission
                        //this.Publish(this.buffer,this.dataUpdated);
                        //this.dataUpdated(this.buffer);

                        //Console.WriteLine($"Client Received info: {Encoding.ASCII.GetString(buffer, 0, bytesDataRec)}");
                        //Console.WriteLine($"Client Received info: {Encoding.ASCII.GetString(buffer)}");
                    }


                    // Release the socket. 
                    socket.Shutdown(SocketShutdown.Both);
                    socket.Close();
                }
                catch (ArgumentNullException ane)
                {
                    Console.WriteLine("ArgumentNullException : {0}", ane.ToString());
                }
                catch (SocketException se)
                {
                    Console.WriteLine("SocketException : {0}", se.ToString());
                }
                catch (Exception e)
                {
                    Console.WriteLine("Unexpected exception : {0}", e.ToString());
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }

            //this.socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            //socket.Connect(remoteEP);
            //byte[] msg = Encoding.ASCII.GetBytes("This is a test by Silver<EOF>");
            //int bytesSent = socket.Send(msg);

            // Receive the response from the remote device. 
            //int bytesRec = socket.Receive(this.buffer);
            //Console.WriteLine($"Client Received info: {Encoding.ASCII.GetString(this.buffer, 0, bytesRec)}");

            // Release the socket. 
            //socket.Shutdown(SocketShutdown.Both);
            //socket.Close();
        }

        //---------------Utilities method---------------------------
        private void setPosition(float lat, float lon)
        {
            this.NorS = Direction.North;
            this.EorW = Direction.South;
            if (this.lon > 180)
            {
                lon = (lon - 360) * (-1);
                this.EorW = Direction.West;
            }
            else if (lon < 0 && lon >= -180)
            {
                lon *= -1;
                this.EorW = Direction.West;
            }
            else if (lon < -180)
            {
                lon += 360;
                this.EorW = Direction.East;
            }
            else
            {
                this.lon = lon;
            }

            if (lat < 0)
            {
                lat *= (-1);
                this.NorS = Direction.South;
            }

            this.lonDeg = Convert.ToInt32(lon);//int(lon);
            this.latDeg = Convert.ToInt32(lat);
            this.lonMin = (lon - this.lonDeg) * 60;
            this.latMin = (lat - this.latDeg) * 60;
        }
        private string getMountPointString()
        {
            string mountPointString = $"GET {this.mountpoint} HTTP/1.1\r\nUser-Agent: {NtripClient.useragent}\r\nAuthorization: Basic {this.user}\r\n";
            if (this.host || this.V2)
            {
                string hostString = $"Host: {this.caster}:{this.port}\r\n";
                mountPointString += hostString;
            }
            if (this.V2)
            {
                mountPointString += "Ntrip-Version: Ntrip/2.0\r\n";
            }
            mountPointString += "\r\n";
            if (this.verbose)
            {
                Console.WriteLine(mountPointString);
            }
            return mountPointString;
            //Console.Write(mountPointString);
            //Console.WriteLine("1");
            //Console.Write("\r\n");
            //Console.WriteLine("1");
        }
        private string getGGAString()
        {
            DateTime now = DateTime.Now;
            string flagN = "N";
            if (this.NorS == Direction.North)
            {
                flagN = "N";
            }
            else
            {
                flagN = "S";
            }
            string flagE = "E";
            if (this.EorW == Direction.East)
            {
                flagE = "E";
            }
            else
            {
                flagE = "W";
            }
            string ggaString = string.Format("GPGGA,{0:00}{1:00}{2:00.00},{3:00}{4:00.00000000},{5},{6:000}{7:000.00000000},{8},1,05,0.19,+00400,M,{9:0.000},M,,", now.Hour, now.Minute, now.Second, this.latDeg, this.latMin, flagN, this.lonDeg, this.lonMin, flagE, this.height);//"GPGGA,%02d%02d%04.2f,%02d%011.8f,%1s,%03d%011.8f,%1s,1,05,0.19,+00400,M,%5.3f,M,," % \(now.hour, now.minute, now.second, self.latDeg, self.latMin, self.flagN, self.lonDeg, self.lonMin, self.flagE, self.height);

            string checksum = this.calcultateCheckSum(ggaString);
            if (this.verbose)
            {
                Console.WriteLine($"${ggaString}*{checksum}\r\n");
            }
            return $"${ggaString}*{checksum}\r\n";
        }
        private string calcultateCheckSum(string stringToCheck)
        {
            int xsum_calc = 0;
            foreach (char c in stringToCheck.ToArray())
            {
                xsum_calc = xsum_calc ^ Convert.ToInt32(c);
            }
            return String.Format("{0:X2}", xsum_calc);//"%02X" % xsum_calc;
        }

    }

    public static class Utility
    {
        public static string ToBase64String(this string s)
        {
            return Convert.ToBase64String(Encoding.ASCII.GetBytes(s));
        }
    }
}
