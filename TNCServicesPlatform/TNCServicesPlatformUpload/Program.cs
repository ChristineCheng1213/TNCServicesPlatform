using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text;
using System.Net.Http.Headers;
using System.Web;
using System.Diagnostics;
using Microsoft.Cognitive.CustomVision.Training.Models;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using System.IO;
using TNCServicesPlatform.StorageAPI.Models;

namespace TNCServicesPlatformUpload
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

            string rootDirectory = @"C:\TNC";
            DirectoryInfo root = new DirectoryInfo(rootDirectory);
            WalkDirectoryTree(root);
        }

        static void WalkDirectoryTree(System.IO.DirectoryInfo root)
        {
            FileInfo[] files = null;
            DirectoryInfo[] subDirs = null;

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
                        if (fi.Extension == ".JPG" || fi.Extension == ".AVI")
                        {
                            AnimalImage image = getImageInfo(lines, fi);
                            Console.WriteLine(image.ImageName + " " + image.Tag);
                            AnimalImage result = UploadImage(fi.FullName, image).Result;
                        }
                    }
                }

                subDirs = root.GetDirectories();

                foreach (System.IO.DirectoryInfo dirInfo in subDirs)
                {
                    WalkDirectoryTree(dirInfo);
                }
            }
        }


        static AnimalImage getImageInfo(string[] lines, FileInfo fi)
        {
            AnimalImage image = new AnimalImage();
            foreach (var line in lines)
            {
                var values = line.Split(',');
                if (fi.Name.Equals(values[0]+"."+values[2]))
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

    }
}
