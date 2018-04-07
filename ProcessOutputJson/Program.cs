using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using Accord.Video.FFMPEG;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ProcessOutputJson
{
    public static class ProcessJsonProgram
    {
        //TODO: Add Output to output file paths.
        //tODO: Test .json config creation /c switch.
        //TODO: Change jpeg ffmpeg args to take more than one jpeg, experiment with frames count.
        //TODO: Adjust time format for jpeg ffmpeg args to be exactly when text detected to the millisecond.
        //TODO: Add option to reduce the quality of output snippets to next lowest quality, or manual quality/scale. Start with 720p if 1080p initially etc.

        static string _jsonOcrInputfile = @"c:\SupportFiles\OCR\test.mp4.ocrinput.json";
        static string _jsonOcrOutputfile = @"c:\SupportFiles\OCR\Output\test_videoocr.json";
        static string _videofile = @"c:\SupportFiles\OCR\test.mp4";
        private static dynamic Jobs;
        //static Regex _regExPSN = new Regex("PSN",
        //    RegexOptions.Compiled | RegexOptions.Multiline);
        static Regex _regExCODE = new Regex(
                        "([\\w\\d]{4,5}\\S?\\-\\S?){2}[\\w\\d]{3,5}",
            RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly string ExeName = Path.GetFileName(Assembly.GetExecutingAssembly().Location);
        static readonly string Ffmpeg = Path.GetDirectoryName(_videofile) + "\\ffmpeg\\bin\\ffmpeg.exe";

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


            var matches = ExtractRegExMatches(_jsonOcrOutputfile);

            ProcessOutputs(_videofile,needJpegs, needVideo, matches);


            Console.ForegroundColor = ConsoleColor.Black;
            Console.WriteLine("> Press enter to exit.");
            Console.ReadLine();
            
        }

        private static void TriggerAzureOCR()
        {
            throw new NotImplementedException();
        }

        private static void CreateVideoInputJson(string language = "English", string textOrientation = "Auto",string width="1920", string height="1080")
        {
            using (var v = new VideoFileReader())
            {
                v.Open(_videofile);
                try{  height=v.Height.ToString();}catch{}
                try{ width=v.Width.ToString();}catch{}
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
                       ""Width"": " + width + @",
                       ""Height"": " + height + @"
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

        public static void ProcessOutputs(string _videofile,bool needJpegs, bool needVideo, List<JToken> matches,string clipDuration="3")
        {
            string uNow = $"{DateTime.UtcNow:O}".Replace(":", "");

            
            long i = 1;
            foreach (dynamic item in matches)
            {
                double start = item.start;
                double end = item.end;
                double timescale = item.timescale;
                var dt = TimeSpan.FromMilliseconds(Math.Floor((start*1000) / timescale));//.FromSeconds(Math.Floor(start / timescale));
                var t = dt.ToString("g");
                var de = TimeSpan.FromMilliseconds(Math.Ceiling((1000*end) / timescale));
                var tso = de - dt;
                var tsot = tso.TotalSeconds;
                if (tsot < 3) tsot = double.Parse(clipDuration); //make min_vid_clip parameter
                var origName = Path.GetFileNameWithoutExtension(_videofile);
                var newSnippetFile = _videofile.Replace(origName??string.Empty, $"out_{uNow}_{i}_{origName}");
                var acceleration=""; // -hwaccel cuvid
#if DEBUG
                acceleration = "-threads 8 ";
#endif
                if (needVideo)
                {
                    var args = $"{acceleration}-i {_videofile} -c:av copy -ss {t} -t {tsot} {newSnippetFile}";
                    _procsToSpawn.Add(args);
                }

                if (needJpegs)
                {
                    var args = $"{acceleration}-ss {t} -i {_videofile} -qscale:v 4 -frames:v 1 -huffman optimal {newSnippetFile}.jpg";
                    _procsToSpawn.Add(args);
                }
                using (var f = File.OpenWrite(newSnippetFile + ".txt"))
                {
                    using (var bw = new BinaryWriter(f))
                    {
                        var str = ($"\r\n{uNow} {item.text} top:{item.top} left:{item.left} width:{item.width} height:{item.height}\r\n");
                        bw.Write(str);
                        bw.Flush();
                    }

                }


                i++;
            }

            Console.WriteLine($"*** Processed {i - 1} records.");
            SpawnProcesses();
        }

        private static void SpawnProcesses()
        {
            Console.WriteLine("Spawning processes...");
            var c = 1;
            foreach (var item in _procsToSpawn)
            {
                var p = Process.Start(Ffmpeg, item);
                if(p==null)continue;
                Console.Write(
                    $"Awaiting finish of process #{c}/{_procsToSpawn.Count} handle:{p.Handle} [Args: ffmpeg {item} ]\n...");
                do
                {
                    Thread.Sleep(500);
                    Console.Write(".");
                } while (!p.HasExited);

                Console.WriteLine($"Spawned process has finished - Code {p.ExitCode}");
                c++;
            }
        }
        
        public static List<JToken> ExtractRegExMatches(string _jsonOcrOutputfile)
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
                    int counter = 0;
                    foreach (dynamic evtArray in item.events)
                    {
                        
                        if (evtArray == null) continue;
                        foreach (dynamic evt in evtArray)
                        {
                            var starttime = (item.start) + ((item.interval) * counter);
                            var endtime = starttime + ((item.interval));// * (counter + 1));
                             counter = counter + 1;
                            Console.WriteLine($"Counter: {counter}");
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

                                        #region json example
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
                                        #endregion json example
                                    }

                                }
                            }
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
            Console.WriteLine($"{ExeName} <OCR input json file> <input video> /c");
            Console.ForegroundColor = ConsoleColor.Black;
            Console.WriteLine("");
            Console.WriteLine("where the input file should be in the same folder as the video file.");
            Console.WriteLine("\t\tOR...");
            Console.WriteLine("");
            Console.WriteLine("2) Takes an Azure OCR Output json file, matches text against a regex,");
            Console.WriteLine();
            Console.WriteLine("Syntax:");
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"{ExeName} <OCR output json file> <input video> /j /v");
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
            Console.WriteLine($"{ExeName} {String.Join(" ", args)}");
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
