using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace DS4Windows;

internal sealed record PortableBrokerPorts(int Usb, int Api)
{
    internal const int DefaultUsbPort = 3241;
    internal static readonly PortableBrokerPorts Default = new(DefaultUsbPort, ViiperSetupManager.ApiPort);
    internal static PortableBrokerPorts Current => PortableBrokerContext.Current?.Ports ?? Default;

    private const string UsbArgument = "--usb.addr=127.0.0.1:";
    private const string ApiArgument = "--api.addr=127.0.0.1:";
    internal string UsbAddressArgument => UsbArgument + Usb.ToString(CultureInfo.InvariantCulture);
    internal string ApiAddressArgument => ApiArgument + Api.ToString(CultureInfo.InvariantCulture);

    internal static PortableBrokerPorts FromArguments(IReadOnlyList<string> arguments)
    {
        int usb = ReadPort(UsbArgument);
        int api = ReadPort(ApiArgument);
        return usb != 0 && api != 0 && usb != api ? new(usb, api) : null;

        int ReadPort(string prefix)
        {
            string argument = arguments.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));
            return argument != null && int.TryParse(argument.AsSpan(prefix.Length),
                NumberStyles.None, CultureInfo.InvariantCulture, out int port) && port is >= 1024 and <= 65535
                ? port : 0;
        }
    }

    internal static PortableBrokerPorts Select(bool useFallback, PortableBrokerPorts preferred = null)
    {
        if (!useFallback)
        {
            preferred ??= Default;
            try
            {
                using var usb = Bind(preferred.Usb);
                using var api = Bind(preferred.Api);
                return preferred;
            }
            catch (SocketException error) when (error.SocketErrorCode is SocketError.AccessDenied or SocketError.AddressAlreadyInUse)
            {
                // A Windows excluded port may reject bind even with no listener.
            }
        }

        // Let Windows select two distinct bindable ports. Keep the USB socket
        // bound while selecting the API port, then release both for VIIPER.
        try
        {
            using var availableUsb = Bind(0);
            using var availableApi = Bind(0);
            return new(((IPEndPoint)availableUsb.LocalEndPoint).Port, ((IPEndPoint)availableApi.LocalEndPoint).Port);
        }
        catch (SocketException error)
        {
            throw new PortableBrokerStartupException(
                "Windows could not allocate two local ports for portable VIIPER. Free a local TCP port and retry Install / Repair VIIPER.", error);
        }
    }

    private static Socket Bind(int port)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            socket.ExclusiveAddressUse = true;
            socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
