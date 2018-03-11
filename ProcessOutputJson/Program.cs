using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net.Mime;
using System.Text.RegularExpressions;
using System.Threading;
using Accord.Video.FFMPEG;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ProcessOutputJson
{
    public static class ProcessJsonProgram
    {
        static string _jsonOcrInputfile = @"c:\SupportFiles\OCR\config.json";
        static string _jsonOcrOutputfile = @"c:\SupportFiles\OCR\Output\presentation_videoocr.json";
        static string _videofile = @"c:\SupportFiles\OCR\presentation.mp4";
        private static dynamic Jobs;
        static Regex _regExPSN = new Regex("PSN",
            RegexOptions.Compiled | RegexOptions.Multiline);
        static Regex _regExCODE = new Regex(
                        "([\\w\\d]{4,5}\\S?\\-\\S?){2}[\\w\\d]{3,5}",
            RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly string _exeName = Path.GetFileName(Assembly.GetExecutingAssembly().Location);

        private static List<string> _procsToSpawn=new List<string>();

        public static void Main(string[] args)
        {
            bool needJpegs = true;
            bool needVideo = false;

            PrintScreenOfBgColor(ConsoleColor.Gray);
            Console.ForegroundColor = ConsoleColor.Black;
            //if (args.Length == 0 || args.All(string.IsNullOrWhiteSpace))
            //    PrintArgsAndSyntaxAndAwaitEnterKeyThenQuit(args);

            if (args.Length != 0 && (args[0][0] != '/'))
            {
                if (args.Length == 1)
                    PrintArgsAndSyntaxAndAwaitEnterKeyThenQuit(args);
                if (args[1][0] != '/')
                    PrintArgsAndSyntaxAndAwaitEnterKeyThenQuit(args);
                _jsonOcrOutputfile = args[0];
                _videofile = args[1];

            }

            if (args.Any(x => x.ToLowerInvariant() == "/v")) needVideo = true;
            if (needVideo && args.All(x => x.ToLowerInvariant() != "/j"))
                needJpegs = false;

            if (!(File.Exists(_jsonOcrOutputfile) && File.Exists(_videofile)))
                PrintArgsAndSyntaxAndAwaitEnterKeyThenQuit(args);

            if (args.Any(x => x.ToLowerInvariant() == "/c"))
            {
                CreateVideoInputJson();
                TriggerAzureOCR();
                PrintScreenOfBgColor(ConsoleColor.Black);
                Console.WriteLine("*** Done.");
                Console.WriteLine("*** Created input json and queued azure ocr job.");
                Environment.Exit(0);
            }


            var matches = ExtractRegExMatches();

            ProcessOutputs(needJpegs, needVideo, matches);


            Console.ForegroundColor = ConsoleColor.Black;
            Console.WriteLine("> Press enter to exit.");
            Console.ReadLine();
            
        }

        private static void TriggerAzureOCR()
        {
            throw new NotImplementedException();
        }

        private static void CreateVideoInputJson(string language = "English", string textOrientation = "Auto")
        {
            using (var v = new VideoFileReader())
            {
                v.Open(_videofile);
                var jsonString =
                #region json file contents with video width+height inserted
@"  {
        ""Version"":1.0, 
        ""Options"": 
        {
            ""AdvancedOutput"":""true"",
            ""Language"":""" + language + @""", 
            ""TimeInterval"":""00:00:00.5"",
            ""TextOrientation"":""" + textOrientation + @""",
            ""DetectRegions"": [
                    {
                       ""Left"": 1,
                       ""Top"": 1,
                       ""Width"": " + v.Width + @",
                       ""Height"": " + v.Height + @"
                    }
             ]
        }
    }";
                #endregion

                using (var f = new StreamWriter(_jsonOcrInputfile, false, Encoding.Default))
                {
                    f.WriteLine(jsonString);

                }
                v.Close();
            } // end of using VideoFileReader

        }

        private static void ProcessOutputs(bool needJpegs, bool needVideo, List<JToken> matches)
        {
            string uNow = $"{DateTime.UtcNow:O}".Replace(":", "");

            var _ffmpeg = Path.GetDirectoryName(_videofile) + "\\ffmpeg\\bin\\ffmpeg.exe";
            long i = 1;
            foreach (dynamic item in matches)
            {
                double start = item.start;
                double end = item.end;
                double timescale = item.timescale;
                string text = item.text;
                //var dt = new DateTime();
                //dt = dt.AddMilliseconds(start);
                var dt = TimeSpan.FromSeconds(Math.Floor(start / timescale));
                var t = dt.ToString("g");
                var de = TimeSpan.FromSeconds(Math.Ceiling(end / timescale));
                var tso = de - dt;
                var tsot = tso.TotalSeconds;
                if (tsot < 3) tsot = 3;
                var origName = Path.GetFileNameWithoutExtension(_videofile);
                var _newSnippetFile = _videofile.Replace(origName, $"out_{uNow}_{i}_{origName}");
                if (needVideo)
                {

                    var args = $"-hwaccel cuvid -i {_videofile} -c:av copy -ss {t} -t {tsot} {_newSnippetFile}";
                    _procsToSpawn.Add(args);
                }

                if (needJpegs)
                {
                    var args = $"-ss {t} -i {_videofile} -qscale:v 4 -frames:v 1 -huffman optimal {_newSnippetFile}.jpg";
                    _procsToSpawn.Add(args);
                }
                using (var f = File.OpenWrite(_newSnippetFile + ".txt"))
                {
                    using (var bw = new BinaryWriter(f))
                    {
                        var str = ($"\r\n{uNow} {item.text} top:{item.top} left:{item.left} width:{item.width} height:{item.height}\r\r");
                        bw.Write(str);
                        bw.Flush();
                    }

                }


                i++;
            }

            Console.WriteLine($"*** Processed {i - 1} records.");
            Console.WriteLine("Spawning processes...");
            var c = 0;
            foreach (var item in _procsToSpawn)
            {
                var p = Process.Start(_ffmpeg, item);
                Console.Write($"Awaiting finish of process #{c} handle:{p.Handle} [Args: ffmpeg {item} ]\n...");
                do
                {
                    Thread.Sleep(500);
                    Console.Write(".");
                } while (!p.HasExited);
                Console.WriteLine("Finished spawned process.");
                c++;
            }

        }

        private static void AwaitProgress()
        {
            throw new NotImplementedException();
        }

        private static void QueueBatchOfTranscodingJobs()
        {
            throw new NotImplementedException();
        }

        private static void CreateConfigsForMatches()
        {
            throw new NotImplementedException();
        }

        private static List<JToken> ExtractRegExMatches()
        {
            var matches = new List<JToken>();
            var fileStream = new FileStream(_jsonOcrOutputfile, FileMode.Open);
            var streamJson = new StreamReader(fileStream);
            var strJson = streamJson.ReadToEnd();
            var isMatch = _regExCODE.IsMatch(strJson);
            if (isMatch)
            {
                dynamic jsonVal = JValue.Parse(strJson);


                foreach (dynamic item in jsonVal.fragments)
                {
                    if (item.events == null) continue;
                    foreach (dynamic evtArray in item.events)
                    {
                        if (evtArray == null) continue;
                        var i = 0;
                        foreach (dynamic evt in evtArray)
                        {
                            var starttime = (item.start) + ((item.interval) * i);
                            var endtime = starttime + ((item.interval) * (i + 1));
                            if (evt.region == null) continue;
                            if (evt.region.lines == null) continue;

                            foreach (dynamic lineOfText in evt.region.lines)
                            {
                                if (lineOfText.text != null)
                                {
                                    if (_regExCODE.IsMatch(lineOfText.text.ToString()))
                                    {
                                        lineOfText.start = starttime;
                                        lineOfText.end = endtime;
                                        lineOfText.timescale = jsonVal.timescale;
                                        matches.Add(lineOfText);


                                        Console.ForegroundColor = ConsoleColor.DarkBlue;
                                        Console.WriteLine(
                                            $"Start: {starttime} End: {endtime}  " +
                                            JsonConvert.SerializeObject(lineOfText));
                                        Console.Beep(620, 30);


                                        //  "version": 1,
                                        // "timescale": 90000,
                                        // "offset": 0,
                                        // "framerate": 60,
                                        // "width": 1920,
                                        // "height": 1080,
                                        // "totalDuration": 39469500,
                                        // "fragments": [
                                        // {
                                        //   "start": 0,
                                        //   "duration": 270000,
                                        //   "interval": 135000,
                                        //   "events": [
                                        //    [
                                        //      {
                                        //          "region": {
                                        //              "language": "English",
                                        //              "orientation": "Up",
                                        //              "lines": [
                                        //              {
                                        //                  "text":
                                    }

                                }
                            }
                            i++;
                        }
                    }
                }

            }

            return matches;
        }

        private static void PrintArgsAndSyntaxAndAwaitEnterKeyThenQuit(string[] args, ConsoleColor bgcolor = ConsoleColor.White)
        {
            PrintScreenOfBgColor(bgcolor);
            Console.BackgroundColor = bgcolor;
            Console.ForegroundColor = ConsoleColor.Black;
            Console.WriteLine("1) Creates an Azure OCR input json file, from an input video file.");
            Console.WriteLine("");
            Console.WriteLine("Syntax:");
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"{_exeName} <OCR input json file> <input video> /c");
            Console.ForegroundColor = ConsoleColor.Black;
            Console.WriteLine("");
            Console.WriteLine("where the input file should be in the same folder as the video file.");
            Console.WriteLine("\t\tOR...");
            Console.WriteLine("");
            Console.WriteLine("2) Takes an Azure OCR Output json file, matches text against a regex,");
            Console.WriteLine();
            Console.WriteLine("Syntax:");
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"{_exeName} <OCR output json file> <input video> /j /v");
            Console.ForegroundColor = ConsoleColor.Black;
            Console.WriteLine();
            Console.WriteLine("Where all parameters are optional, however if specifying the json,"
                              + "\n one must also specify the input video and vice-versa.\n" +
                " /j = jpeg and /v = video, both are optional and request output accordingly.");
            Console.WriteLine();
            Console.WriteLine($"Default for json file: {_jsonOcrOutputfile}");
            Console.WriteLine($"Default for video file: {_videofile}");
            Console.WriteLine($"Default for output format is jpeg.");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("You supplied:");
            Console.WriteLine($"{_exeName} {String.Join(" ", args)}");
            //Console.WriteLine($"{String.Join(" ", Environment.GetCommandLineArgs())}" );
            Console.WriteLine("\r\n");
            Console.ForegroundColor = ConsoleColor.Black;
            Console.WriteLine("\r\n");
            Console.Beep(440, 120);
            Console.WriteLine("Press enter to exit.");
            Console.WriteLine("\n");
            Console.ReadLine();

            Environment.Exit(160);
            //https://msdn.microsoft.com/en-us/library/windows/desktop/ms681382(v=vs.85).aspx
            //ERROR_INVALID_PARAMETER
            //87(0x57)
            //The parameter is incorrect.
            //
            //ERROR_BAD_ARGUMENTS
            //160(0xA0)
            //One or more arguments are not correct.
        }

        private static void PrintScreenOfBgColor(ConsoleColor bgColor)
        {
            Console.BackgroundColor = bgColor;
            var s = "";
            for (int i = 0; i < Console.WindowWidth; i++)
            {
                s += " ";
            }

            for (int i = 0; i < Console.WindowHeight + 1; i++)
            {
                Console.Write(s);
            }
            Console.SetCursorPosition(0, 0);
        }
    }
}
