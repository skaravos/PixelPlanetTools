﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;



namespace PixelPlanetBot
{
    using Pixel = ValueTuple<short, short, PixelColor>;
    static partial class Program
    {
        static readonly string appFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PixelPlanetBot");

        static readonly string filePath = Path.Combine(appFolder, "guid.bin");

        static Guid userGuid;

        static string Fingerprint => userGuid.ToString("N");

        //TODO proxy, 1 guid per proxy, save with address hash and last usage timedate, clear old

        public static bool DefendMode { get; set; } = false;

        public static bool EmptyLastIteration { get; set; }

        private static PixelColor[,] Pixels { get; set; }

        private static short leftX, topY;

        public static bool IsPicturePart(short x, short y)
        {
            return Pixels[x - leftX, y - topY] != PixelColor.None;
        }

        static void Main(string[] args)
        {
            Bitmap image = null;
            PlacingOrderMode order = PlacingOrderMode.Random;
            Task<PixelColor[,]> pixelsTask;
            try
            {
                leftX = short.Parse(args[0]);
                topY = short.Parse(args[1]);
                using (WebClient wc = new WebClient())
                {
                    byte[] data = wc.DownloadData(args[2]);
                    MemoryStream ms = new MemoryStream(data);
                    image = new Bitmap(ms);
                }
                if (args.Length > 3)
                {
                    DefendMode = args[3].ToLower() == "y";
                }
                if (args.Length > 4)
                {
                    switch (args[4].ToUpper())
                    {
                        case "R":
                            order = PlacingOrderMode.FromRight;
                            break;
                        case "L":
                            order = PlacingOrderMode.FromLeft;
                            break;
                        case "T":
                            order = PlacingOrderMode.FromTop;
                            break;
                        case "B":
                            order = PlacingOrderMode.FromBottom;
                            break;
                    }
                }
                pixelsTask = ImageProcessing.ToPixelWorldColors(image);
            }
            catch
            {
                Console.WriteLine("Parameters: [left x: -32768..32767] [top y: -32768..32767] [image URL] [defend mode: Y/N = N]");
                return;
            }
            try
            {
                SetUserGuid();
                InteractionWrapper wrapper = new InteractionWrapper(Fingerprint);
                ushort w, h;
                try
                {
                    checked
                    {
                        w = (ushort)image.Width;
                        h = (ushort)image.Height;
                        short check;
                        check = (short)(leftX + w);
                        check = (short)(topY + h);
                    }
                }
                catch
                {
                    throw new Exception("Out of the range, check image size and coordinates");
                }
                ChunkCache cache = new ChunkCache(leftX, topY, w, h, wrapper);
                Pixels = pixelsTask.Result;
                IEnumerable<int> allY = Enumerable.Range(0, h);
                IEnumerable<int> allX = Enumerable.Range(0, w);
                Pixel[] nonEmptyPixels = allX.
                    SelectMany(X => allY.Select(Y =>
                        (X: (short)(X + leftX), Y: (short)(Y + topY), C: Pixels[X, Y]))).
                    Where(xy => xy.C != PixelColor.None).ToArray();
                IEnumerable<Pixel> pixelsToCheck;
                switch (order)
                {
                    case PlacingOrderMode.FromLeft:
                        pixelsToCheck = nonEmptyPixels.OrderBy(xy => xy.Item1).ToList();
                        break;
                    case PlacingOrderMode.FromRight:
                        pixelsToCheck = nonEmptyPixels.OrderByDescending(xy => xy.Item1).ToList();
                        break;
                    case PlacingOrderMode.FromTop:
                        pixelsToCheck = nonEmptyPixels.OrderBy(xy => xy.Item2).ToList();
                        break;
                    case PlacingOrderMode.FromBottom:
                        pixelsToCheck = nonEmptyPixels.OrderByDescending(xy => xy.Item2).ToList();
                        break;
                    default:
                        Random rnd = new Random();
                        for (int i = 0; i < nonEmptyPixels.Length; i++)
                        {
                            int r = rnd.Next(i, nonEmptyPixels.Length);
                            Pixel tmp = nonEmptyPixels[r];
                            nonEmptyPixels[r] = nonEmptyPixels[i];
                            nonEmptyPixels[i] = tmp;
                        }
                        pixelsToCheck = nonEmptyPixels;
                        break;
                }
                do
                {
                    EmptyLastIteration = true;
                    foreach ((short x, short y, PixelColor color) in pixelsToCheck)
                    {
                        PixelColor actualColor = cache.GetPixel(x, y);
                        if (color != actualColor)
                        {
                            EmptyLastIteration = false;
                            double cd = wrapper.PlacePixel(x, y, color);
                            Task.Delay(TimeSpan.FromSeconds(cd)).Wait();
                        }
                    }
                    if (DefendMode)
                    {
                        if (!EmptyLastIteration)
                        {
                            LogLineToConsole("Building iteration finished", ConsoleColor.Yellow);
                        }
                        else
                        {

                            LogLineToConsole("\tNo changes was made, waiting 1 min before next check", ConsoleColor.Green);
                            Task.Delay(TimeSpan.FromMinutes(1D)).Wait();
                        }
                    }
                    else
                    {
                        LogLineToConsole("Building finished", ConsoleColor.Green);
                    }
                }
                while (DefendMode);
            }
            catch
            {
                var process = Process.GetCurrentProcess();
                string fullPath = process.MainModule.FileName;
                args[2] = $"\"{args[2]}\"";
                Process.Start(fullPath, string.Join(" ", args));
            }

        }

        public static void LogLineToConsole(string msg, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(msg);
            Console.ForegroundColor = ConsoleColor.White;
        }

        private static void SetUserGuid()
        {
            if (File.Exists(filePath))
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                if (bytes.Length == 16)
                {
                    userGuid = new Guid(bytes);
                    return;
                }
            }
            else
            {
                Directory.CreateDirectory(appFolder);
                userGuid = Guid.NewGuid();
                File.WriteAllBytes(filePath, userGuid.ToByteArray());
            }
        }
    }
}
