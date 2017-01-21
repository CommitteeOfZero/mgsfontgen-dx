﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace MgsFontGenDX
{
    public static class Program
    {
        private const int DefaultFontSize = 36;
        private const int DefaultBaselineOriginX = 1;
        private const int DefaultBaselineOriginY = -4;

        private const string OutputName = "FONT";
        private const string OutlineName = "font-outline";
        private const string PngExtenstion = ".png";
        private const string DdsExtension = ".dds";

        private static readonly Dictionary<string, Action<Arguments>> s_commands;

        static Program()
        {
            s_commands = new Dictionary<string, Action<Arguments>>()
            {
                ["generate"] = GenerateFont
            };
        }

        private static int Run(Arguments arguments)
        {
            Action<Arguments> handler;
            if (!s_commands.TryGetValue(arguments.Command.ToLowerInvariant(), out handler))
            {
                Console.WriteLine("Unknown command.");
                return -1;
            }

            try
            {
                handler(arguments);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return -1;
            }

            return 0;
        }

        private static void GenerateFont(Arguments arguments)
        {
            string extension = arguments.ImageFormat == ImageFormat.Png ? PngExtenstion : DdsExtension;
            string outputFileName = OutputName + extension;
            string outlineFileName = OutlineName + extension;

            var charset = File.ReadAllText(arguments.CharsetFileName);
            var compoundCharTable = ReadCompoundCharacterTable(arguments.CompoundCharTableFileName);

            using (var outputFile = File.Create(outputFileName))
            using (var outlineFile = File.Create(outlineFileName))
            using (var textRenderer = new TextRenderer())
            using (var widthTableFile = File.Create("widths.bin"))
            using (var widthWriter = new BinaryWriter(widthTableFile))
            {
                byte[] widths;
                var font = textRenderer.GenerateBitmapFont(charset, compoundCharTable, arguments.ImageFormat, out widths,
                    false, arguments.FontFamily, arguments.FontSize, arguments.BaselineOriginX, arguments.BaselineOriginY);

                byte[] _;
                var outline = textRenderer.GenerateBitmapFont(charset, compoundCharTable, arguments.ImageFormat, out _,
                    true, arguments.FontFamily, arguments.FontSize, arguments.BaselineOriginX, arguments.BaselineOriginY);

                font.CopyTo(outputFile);
                font.Dispose();
                outline.CopyTo(outlineFile);
                outline.Dispose();

                widthWriter.Write(widths);
            }
        }

        public static void Main(string[] args)
        {
            if (!args.Any())
            {
                return;
            }

            Arguments parsedArgs = null;
            try
            {
                parsedArgs = ParseArguments(args);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Environment.Exit(-1);
            }

            int exitCode = Run(parsedArgs);
            Environment.Exit(exitCode);
        }

        private static Arguments ParseArguments(string[] args)
        {
            string command = args[0];
            var dictionary = ArgArrayToDictionary(args.Skip(1).ToArray());

            string compoundCharacters;
            string strImageFormat;
            string strFontSize, strOffsetX, strOffsetY;
            string strBaselineOriginX, strBaselineOriginY;
            Arguments parsedArgs;
            try
            {
                parsedArgs = new Arguments()
                {
                    Command = command,
                    CharsetFileName = dictionary["charset"],
                    FontFamily = dictionary["font-family"]
                };

                dictionary.TryGetValue("compound-characters", out compoundCharacters);
                dictionary.TryGetValue("image-format", out strImageFormat);
                dictionary.TryGetValue("font-size", out strFontSize);
                dictionary.TryGetValue("offsetx", out strOffsetX);
                dictionary.TryGetValue("offsety", out strOffsetY);
                dictionary.TryGetValue("baseline-originx", out strBaselineOriginX);
                dictionary.TryGetValue("baseline-originy", out strBaselineOriginY);

                switch ("." + strImageFormat?.ToLowerInvariant())
                {
                    case DdsExtension:
                        parsedArgs.ImageFormat = ImageFormat.Dds;
                        break;

                    case PngExtenstion:
                    default:
                        parsedArgs.ImageFormat = ImageFormat.Png;
                        break;
                }

                parsedArgs.CompoundCharTableFileName = compoundCharacters;
                parsedArgs.FontSize = int.Parse(strFontSize ?? DefaultFontSize.ToString());
                parsedArgs.OffsetX = int.Parse(strOffsetX ?? "0");
                parsedArgs.OffsetY = int.Parse(strOffsetY ?? "0");
                parsedArgs.BaselineOriginX = int.Parse(strBaselineOriginX ?? DefaultBaselineOriginX.ToString());
                parsedArgs.BaselineOriginY = int.Parse(strBaselineOriginY ?? DefaultBaselineOriginY.ToString());
            }
            catch
            {
                throw new ArgumentException("Insufficient arguments.");
            }

            return parsedArgs;
        }

        private static IDictionary<string, string> ArgArrayToDictionary(string[] args)
        {
            var dictionary = new Dictionary<string, string>();
            for (int i = 0; i < args.Length; i += 2)
            {
                if (i + 1 >= args.Length || !args[i].StartsWith("-"))
                {
                    throw new ArgumentException();
                }

                string key = args[i].StartsWith("--") ? args[i].Remove(0, 2) : args[i].Remove(0, 1);
                string value = args[i + 1];
                dictionary[key.ToLowerInvariant()] = value;
            }

            return dictionary;
        }

        private sealed class Arguments
        {
            public string Command { get; set; }
            public string CharsetFileName { get; set; }
            public string CompoundCharTableFileName { get; set; }
            public ImageFormat ImageFormat { get; set; }
            public string FontFamily { get; set; }
            public int FontSize { get; set; }
            public int OffsetX { get; set; }
            public int OffsetY { get; set; }
            public int BaselineOriginX { get; set; }
            public int BaselineOriginY { get; set; }
        }

        private static ImmutableDictionary<int, string> ReadCompoundCharacterTable(string fileName)
        {
            var stream = File.Open(fileName, FileMode.Open);
            if (stream == null)
            {
                return ImmutableDictionary<int, string>.Empty;
            }

            using (stream)
            using (var reader = new StreamReader(stream))
            {
                var table = ImmutableDictionary.CreateBuilder<int, string>();
                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine();
                    if (!string.IsNullOrEmpty(line))
                    {
                        var parts = line.Split('=');
                        string strIndex = parts[0].Substring(1, parts[0].Length - 2);
                        string value = parts[1];

                        parts = strIndex.Split('-');
                        if (parts.Length == 1)
                        {
                            int idx = HexStrToInt32(parts[0]);
                            table[idx] = value;
                        }
                        else
                        {
                            int rangeStart = HexStrToInt32(parts[0]);
                            int rangeEnd = HexStrToInt32(parts[1]);
                            for (int i = rangeStart; i <= rangeEnd; i++)
                            {
                                table[i] = value;
                            }
                        }
                    }
                }

                return table.ToImmutable();
            }
        }

        private static int HexStrToInt32(string hexString) => Convert.ToInt32(CleanHexString(hexString), 16);
        private static string CleanHexString(string hexString)
        {
            return hexString.Replace("0x", string.Empty).Replace(" ", string.Empty);
        }
    }
}
