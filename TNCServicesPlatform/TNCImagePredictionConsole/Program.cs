using Microsoft.Cognitive.CustomVision.Training.Models;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using TNCServicesPlatform.StorageAPI.Models;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using System.ComponentModel.Design.Serialization;

//using Microsoft.WindowsAzure.Storage;

namespace TNCImagePredictionConsole
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("TNCImagePredictionConsole.exe <Path_To_Local_Image>");
                Console.WriteLine(@"Example: TNCImagePredictionConsole.exe E:\Monkey.jpg");
            }
 
            List<AnimalImage> imageList = new List<AnimalImage>();
            string rootDirectory = @"C:\TNCTest";
            DirectoryInfo root = new DirectoryInfo(rootDirectory);
            WalkDirectoryTree(root, imageList);
            Console.WriteLine("Finished walking directory");
 
            UploadImageList(imageList);
            string sasUrl = getSasUrl().Result;
            UploadToBlobWithAzcopy(root.FullName, sasUrl);

            // 2. image classification
            //string imageUrl = $"https://tncstorage4test.blob.core.windows.net/animalimages/{image.ImageBlob}";
            //MakePredictionRequestCNTK(imageUrl); 
            //Console.ReadLine();
        }

        static void WalkDirectoryTree(System.IO.DirectoryInfo root, List<AnimalImage> imageList)
        {
            FileInfo[] files = null;
            DirectoryInfo[] subDirs = null;
            Console.WriteLine("Start Walking " + root.FullName);
            try
            {
                files = root.GetFiles("*.*");
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }

            if (files != null)
            {
                string csvFile = "";
                foreach (FileInfo fi in files)
                {
                    if (fi.Extension == ".csv")
                    {
                        try
                        {
                            csvFile = new StreamReader(fi.FullName, Encoding.Default).ReadToEnd();
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                        }
                    }
                }
                foreach (FileInfo fi in files)
                {
                    if (csvFile != "")
                    {
                        var lines = csvFile.Split('\n');
                        if (fi.Extension == ".JPG")
                        {
                            AnimalImage image = getImageInfo(lines, fi);
                            imageList.Add(image);
                        }
                    }
                }
                subDirs = root.GetDirectories();
                foreach (System.IO.DirectoryInfo dirInfo in subDirs)
                {
                    WalkDirectoryTree(dirInfo, imageList);
                }
            }
        }


        static AnimalImage getImageInfo(string[] lines, FileInfo fi)
        {
            AnimalImage image = new AnimalImage();
            foreach (var line in lines)
            {
                var values = line.Split(',');
                if (fi.Name.Equals(values[0] + "." + values[2]))
                {
                    image.ImageName = values[0];
                    image.Tag = values[10];
                    lines = lines.Where(li => li != line).ToArray();
                    return image;
                }
            }
            Console.WriteLine("No infomation found for image: " + fi.Name);
            return image;
        }

        static void UploadToBlobWithAzcopy(string path, string sasUrl)
        {
            using (PowerShell PowerShellInst = PowerShell.Create())
            {
                Console.WriteLine("Start Uploading to Blob...");
                string script = "azcopy cp \"" + path + "\" \"" + sasUrl + "\" --recursive";
                PowerShellInst.AddScript(script);
                Collection<PSObject> PSOutput = PowerShellInst.Invoke();
                foreach (PSObject obj in PSOutput)
                {
                    if (obj != null)
                    {
                        Console.Write(obj.ToString());
                    }
                }
                Console.WriteLine("Done");
                Console.Read();
            }
        }

        //create a new api in api host that is able to recevie a list of animal image meta data , update to cosmos and return a SAS for upload
        static async void UploadImageList(List<AnimalImage> imageList)
        {
            try
            {
                var client = new HttpClient();
                Stopwatch watch = new Stopwatch();
                watch.Start();

                Console.WriteLine("Start uploading image list");
                string uploadUrl = "http://localhost:55464/api/storage/Upload3";

                foreach( AnimalImage image in imageList) {
                    string imageJson = JsonConvert.SerializeObject(image);
                    byte[] byteData = Encoding.UTF8.GetBytes(imageJson);
                    HttpResponseMessage response;

                    using (var content = new ByteArrayContent(byteData))
                    {
                        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                        response = await client.PostAsync(uploadUrl, content);
                    }

                    string responseStr = await response.Content.ReadAsStringAsync();
                    Console.WriteLine(responseStr);
                    Console.WriteLine("\nGet Uploading URL: " + watch.ElapsedMilliseconds);
                    watch.Restart();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                throw ex;
            }
        }

        static async Task<string> getSasUrl()
        {
            try
            {
                var client = new HttpClient();
                string url = "http://localhost:55464/api/storage/GetSasUrl";
                HttpResponseMessage response = await client.GetAsync(url);
                string responseStr = await response.Content.ReadAsStringAsync();
                Console.WriteLine(responseStr);
                return responseStr;
            } 
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                throw ex;
            }
        }

        static async Task<AnimalImage> UploadImage(string imagePath, AnimalImage image)
        {
            try
            {
                var client = new HttpClient();
                Stopwatch watch = new Stopwatch();
                watch.Start();

                Console.WriteLine("Start uploading");

                // 1. Upload meta data to Cosmos DB
                //string uploadUrl = "http://tncapi.azurewebsites.net/api/storage/Upload2";
                string uploadUrl = "http://localhost:55464/api/storage/Upload2";
                string imageJson = JsonConvert.SerializeObject(image);
                byte[] byteData = Encoding.UTF8.GetBytes(imageJson);
                HttpResponseMessage response;

                using (var content = new ByteArrayContent(byteData))
                {
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    response = await client.PostAsync(uploadUrl, content);
                }

                string responseStr = await response.Content.ReadAsStringAsync();
                Console.WriteLine(responseStr);
                AnimalImage imageResponse = JsonConvert.DeserializeObject<AnimalImage>(responseStr);
                Console.WriteLine("\nGet Uploading URL: " + watch.ElapsedMilliseconds);
                watch.Restart();

                // 2. uppload image self to blob storage
                byte[] blobContent = File.ReadAllBytes(imagePath);
                CloudBlockBlob blob = new CloudBlockBlob(new Uri(imageResponse.UploadBlobSASUrl));
                MemoryStream msWrite = new MemoryStream(blobContent);
                msWrite.Position = 0;
                using (msWrite)
                {
                    await blob.UploadFromStreamAsync(msWrite);
                }
                Console.WriteLine("\nImage uploaded: " + watch.ElapsedMilliseconds);

                return imageResponse;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                throw ex;
            }
        }


        static async void MakePredictionRequest(string imageUrl)
        {
            try
            {
                var client = new HttpClient();
                var uri = "http://tncapi.azurewebsites.net/api/prediction/url";

                byte[] byteData = Encoding.UTF8.GetBytes("{\"url\":\"" + imageUrl + "\"}");
                HttpResponseMessage response;

                using (var content = new ByteArrayContent(byteData))
                {
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    response = await client.PostAsync(uri, content);
                }

                string res = await response.Content.ReadAsStringAsync();
                var resObj = JsonConvert.DeserializeObject<ImagePredictionResult>(res);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                throw ex;
            }
        }

        static async void MakePredictionRequestCNTK(string imageUrl)
        {
            try
            {
                Stopwatch watch = Stopwatch.StartNew();
                var client = new HttpClient();
                var uri = "http://tncapi.azurewebsites.net/api/prediction/cntk";

                byte[] byteData = Encoding.UTF8.GetBytes("\"" + imageUrl + "\"");
                HttpResponseMessage response;

                using (var content = new ByteArrayContent(byteData))
                {
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    response = await client.PostAsync(uri, content);
                }

                string res = await response.Content.ReadAsStringAsync();
                var resObj = JsonConvert.DeserializeObject<ImagePredictionResult>(res);
                Console.WriteLine("\nPrediction Time: " + watch.ElapsedMilliseconds + "\n");
                foreach(var pre in resObj.Predictions)
                {
                    Console.WriteLine(pre.Tag + ": " + pre.Probability);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                throw ex;
            }
        }
    }
}
