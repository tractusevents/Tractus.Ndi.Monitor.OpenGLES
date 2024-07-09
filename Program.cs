using BigGustave;
using Microsoft.AspNetCore;
using NewTek;
using NewTek.NDI;
using Silk.NET.OpenGL;
using Silk.NET.SDL;
using Silk.NET.Windowing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static NewTek.NDIlib;

namespace Tractus.Ndi.Monitor;

internal class Program
{
    public static NdiDisplay Display { get; private set; }
    static void Main(string[] args)
    {
        var argsParsed = ParseArguments(args);

        var host = new MonitorWebController();
        host.Rebuild();

        var width = 1280;
        var height = 720;

        width = GetIntArg(argsParsed, "-w", 1280);
        height = GetIntArg(argsParsed, "-h", 720);

        var defaultSource = string.Empty;
        argsParsed.TryGetValue("-src", out defaultSource);

        var ndiDisplay = new NdiDisplay();
        Display = ndiDisplay;
        ndiDisplay.Run(width, height, defaultSource);
    }

    private static int GetIntArg(
        Dictionary<string, string> argsParsed, 
        string arg,
        int defaultValue)
    {
        var toReturn = defaultValue;

        if (argsParsed.TryGetValue(arg, out var widthRaw))
        {
            if (!int.TryParse(widthRaw, out toReturn))
            {
                toReturn = defaultValue;
            }
        }

        return toReturn;
    }

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        var arguments = new Dictionary<string, string>();

        foreach (var arg in args)
        {
            var parts = arg.Split('=', 2);
            if (parts.Length == 2)
            {
                arguments[parts[0]] = parts[1].Trim('"'); // Remove surrounding quotes if any
            }
        }

        return arguments;
    }
}
