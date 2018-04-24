using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace zipkin4net.Utils
{
    internal static class IpUtils
    {

        /// <summary>
        /// Get local IP
        /// (first one which is not loopback if multiple network interfaces are present)
        /// </summary>
        /// <returns>null if not found</returns>
        public static IPAddress GetLocalIpAddress()
        {
            if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
                return null;

#if NETSTANDARD1_4 || NETSTANDARD1_6
            var host = Dns.GetHostEntryAsync(Dns.GetHostName()).GetAwaiter().GetResult();
#else
            var host = Dns.GetHostEntry(Dns.GetHostName());
#endif

            return
                host.AddressList.FirstOrDefault(
                    ip => ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip));
        }

    }
}
