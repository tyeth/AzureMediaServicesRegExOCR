using ProcessOutputJson;
using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.WindowsAzure.MediaServices.Client;
using System.Threading;
using System.Threading.Tasks;
using Accord.Video.FFMPEG;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OCR
{
    public class Program
    {
        // Read values from the App.config file.
        private static readonly string _AADTenantDomain;
        private static readonly string _RESTAPIEndpoint;
        private static readonly string _AMSClientId;
        private static readonly string _AMSClientSecret;

        private static bool NoConsole = false;

        // Field for service context.
        private static CloudMediaContext _context = null;

        static Program()
        {
            _AADTenantDomain = SafelyGetConfigValue("AMSAADTenantDomain");
            _RESTAPIEndpoint = SafelyGetConfigValue("AMSRESTAPIEndpoint");
            _AMSClientId = SafelyGetConfigValue("AMSClientId");
            _AMSClientSecret = SafelyGetConfigValue("AMSClientSecret");
            AzureAdTokenCredentials tokenCredentials =
                new AzureAdTokenCredentials(_AADTenantDomain,
                    new AzureAdClientSymmetricKey(_AMSClientId, _AMSClientSecret),
                    AzureEnvironments.AzureCloudEnvironment);


            _context = new CloudMediaContext(new Uri(_RESTAPIEndpoint), new AzureAdTokenProvider(tokenCredentials));

        }

        public static IAsset GetAsset(string id)
        {
            return _context.Assets.First(x => x.Id == id);
        }

        public static IJob GetJob(string id)
        {
            return _context.Jobs.First(x => x.Id == id);
        }

        private static string SafelyGetConfigValue(string key)
        {
            string a = "";
            try
            {
                a = Environment.GetEnvironmentVariable(key);
            }
            finally
            {
                if (string.IsNullOrEmpty(a)) a = ConfigurationManager.AppSettings[key];
            }
            return a;
        }

        public static void Main(string[] args, bool noConsole=false)
        {
            NoConsole = noConsole;
           if (args.Length != 1) throw new ArgumentException("Missing argument, expecting <video>");// <switch>");
            // Run the OCR job.
            var filename = args[0];
            var jsonFilename = filename + ".ocrinput.json";
            var outputFolder = filename + "Output";
            Directory.CreateDirectory(outputFolder);
            if (!File.Exists(filename) || /*!File.Exists(args[1]) ||*/ !Directory.Exists(outputFolder))
                throw new FileNotFoundException();

           // if(args[1]=="/CreateVideoInputJson")
            CreateVideoInputJson(filename,jsonFilename);

           // if(args[1]== "/UploadOcrAsset")
            var uploadAsset = UploadOcrAsset(filename);

           // if(args[1]=="/RunOcrJob")
            var asset = RunOcrJob(uploadAsset, jsonFilename);
            //@"C:\supportFiles\OCR\presentation.mp4",
            //                      @"C:\supportFiles\OCR\config.json");

            // Download the job output asset.
            DownloadAsset(asset, outputFolder);// @"C:\supportFiles\OCR\Output");
                                          //  ProcessOutputJson.ProcessJsonProgram.Main(new string[0]);
            if(!NoConsole)Console.WriteLine("Press enter to exit");
            if (!NoConsole) Console.ReadLine();
        }

        static IAsset RunOcrJob(IAsset asset, string configurationFile)
        {
            
            IJob job = CreateOcrJob(configurationFile, asset);

            // Use the following event handler to check job progress.  
            job.StateChanged += new EventHandler<JobStateChangedEventArgs>(StateChanged);
            if (!NoConsole) Console.WriteLine($"* Launching new Job {job.Name} on Media Services under Task {job.Tasks[0].Name}");

            // Launch the job.
            job.Submit();

            // Check job execution and wait for job to finish.
            Task progressJobTask = job.GetExecutionProgressTask(CancellationToken.None);

            progressJobTask.Wait();

            // If job state is Error, the event handling
            // method for job progress should log errors.  Here we check
            // for error state and exit if needed.
            if (job.State == JobState.Error)
            {
                ErrorDetail error = job.Tasks.First().ErrorDetails.First();
                if (!NoConsole) Console.WriteLine(string.Format("Error: {0}. {1}",
                                                error.Code,
                                                error.Message));
                return null;
            }

            return job.OutputMediaAssets[0];
        }



        public static void CreateVideoInputJson(string _videofile, string _jsonOcrInputfile, string language = "English", string textOrientation = "Auto", string width = "1920", string height = "1080")
        {
            using (var v = new VideoFileReader())
            {
                v.Open(_videofile);
                try { height = v.Height.ToString(); } catch { }
                try { width = v.Width.ToString(); } catch { }
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


        public static IJob CreateOcrJob(string configurationFile, IAsset asset)
        {
            if (!NoConsole) Console.WriteLine("* Creating new Job on Media Services");
            // Declare a new job.
            IJob job = _context.Jobs.Create("RegExOCRJob" + asset.Id);

            ITask task = CreateAzureMediaOcrTask(configurationFile, job, asset);

            if (!NoConsole) Console.WriteLine("* Associating Assets with Job");
            // Specify the input asset.
            task.InputAssets.Add(asset);

            // Add an output asset to contain the results of the job.
            task.OutputAssets.AddNew("RegExOCROutputAsset" + asset.Id, AssetCreationOptions.None);
            return job;
        }

        private static ITask CreateAzureMediaOcrTask(string configurationFile, IJob job, IAsset asset)
        {
            // Get a reference to Azure Media OCR.
            string MediaProcessorName = "Azure Media OCR";

            var processor = GetLatestMediaProcessorByName(MediaProcessorName);

            // Read configuration from the specified file.
            string configuration = File.ReadAllText(configurationFile);

            // Create a task with the encoding details, using a string preset.
            ITask task = job.Tasks.AddNew("RegExOCRTask" + asset.Id,
                processor,
                configuration,
                TaskOptions.None);
            return task;
        }

        public static IAsset UploadOcrAsset(string inputMediaFilePath)
        {
            if (!NoConsole) Console.WriteLine("* Uploading Asset to Media Services");

            // Create an asset and upload the input media file to storage.
            IAsset asset = CreateAssetAndUploadSingleFile(inputMediaFilePath,
                "RegExOCRInputAsset" + inputMediaFilePath.Replace('/', '!').Replace('\\', '!'),
                AssetCreationOptions.None);
            return asset;
        }

        static IAsset CreateAssetAndUploadSingleFile(string filePath, string assetName, AssetCreationOptions options)
        {
            IAsset asset = _context.Assets.Create(assetName, options);

            var assetFile = asset.AssetFiles.Create(Path.GetFileName(filePath));
            assetFile.Upload(filePath);

            return asset;
        }

        public static void DownloadAsset(IAsset asset, string outputDirectory)
        {
            foreach (IAssetFile file in asset.AssetFiles)
            {
                file.Download(Path.Combine(outputDirectory, file.Name));
            }
        }

        static IMediaProcessor GetLatestMediaProcessorByName(string mediaProcessorName)
        {
            var processor = _context.MediaProcessors
                .Where(p => p.Name == mediaProcessorName)
                .ToList()
                .OrderBy(p => new Version(p.Version))
                .LastOrDefault();

            if (processor == null)
                throw new ArgumentException(string.Format("Unknown media processor",
                                                           mediaProcessorName));

            return processor;
        }

        static private void StateChanged(object sender, JobStateChangedEventArgs e)
        {
            if(!NoConsole)Console.WriteLine("Job state changed event:");
            if(!NoConsole)Console.WriteLine("  Previous state: " + e.PreviousState);
            if(!NoConsole)Console.WriteLine("  Current state: " + e.CurrentState);

            switch (e.CurrentState)
            {
                case JobState.Finished:
                    if(!NoConsole)Console.WriteLine();
                    if(!NoConsole)Console.WriteLine("Job is finished.");
                    if (!NoConsole) Console.WriteLine();
                    break;
                case JobState.Canceling:
                case JobState.Queued:
                case JobState.Scheduled:
                case JobState.Processing:
                    if (!NoConsole) Console.WriteLine("Please wait...\n");
                    break;
                case JobState.Canceled:
                case JobState.Error:
                    // Cast sender as a job.
                    IJob job = (IJob)sender;
                    // Display or log error details as needed.
                    // LogJobStop(job.Id);
                    break;
                default:
                    break;
            }
        }

    }
}