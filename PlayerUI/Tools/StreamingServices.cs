﻿using HtmlAgilityPack;
using Newtonsoft.Json;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PlayerUI.Tools
{
	public class StreamingServices
	{
		public enum Service
		{
			Url,
			Youtube,
			Facebook,
			Vrideo,
			LittlStar,
			Pornhub
		}

		public static bool CheckUrlValid(string url, out string correctedUrl, out Uri uriResult)
		{
			correctedUrl = url;
			if(!correctedUrl.ToLower().StartsWith(@"http://") && !correctedUrl.ToLower().StartsWith(@"https://"))
			{
				correctedUrl = @"http://" + url;
			}
			bool result = Uri.TryCreate(correctedUrl, UriKind.Absolute, out uriResult) && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
			return result;
		}

		public static Service DetectService(Uri uri)
		{
			string domain = string.Join(".", uri.Host.Split('.').Skip(uri.Host.Split('.').Count() - 2));

			switch(domain)
			{
				case "youtube.com": return Service.Youtube;
				case "youtu.be": return Service.Youtube;
				case "facebook.com": return Service.Facebook;
				case "vrideo.com": return Service.Vrideo;
				case "littlstar.com": return Service.LittlStar;
				case "pornhub.com": return Service.Pornhub;
				default: return Service.Url;
			}
		}

		public static bool TryParseVideoFile(Uri uri, out string fileUrl)
		{
			Service detectedService = DetectService(uri);
			Func<Uri, string> TryParse;

			switch (detectedService)
			{
				case Service.Facebook: TryParse = ParseFacebook; break;
				case Service.Youtube: TryParse = ParseYoutube; break;
				case Service.LittlStar: TryParse = ParseLittlStar; break;
				case Service.Vrideo: TryParse = ParseVrideo; break;
				case Service.Pornhub: TryParse = ParsePornhub; break;
				default: TryParse = ParseUrl; break;
			}

			fileUrl = TryParse(uri);

			return !string.IsNullOrWhiteSpace(fileUrl);
		}

		private static HtmlDocument DownloadDocument(Uri uri)
		{
			RestClient client = new RestClient(uri);
			IRestRequest request = new RestRequest(Method.GET);
			request.AddHeader("Accept", "text/html");
            IRestResponse response = client.Execute(request);
			string htmlContent = response.Content;
			HtmlDocument document = new HtmlDocument();
			document.LoadHtml(htmlContent);
			return document;
		}


		public static string ParseFacebook(Uri uri)
		{
			try
			{
				//JSON.parse(/"spherical_hd_src":("[^"]+")/.exec(src)[1])
				//https://www.facebook.com/StarWars/videos/1030579940326940/

				RestClient client = new RestClient(uri);
				IRestRequest request = new RestRequest(Method.GET);
				request.AddHeader("Accept", "text/html");
				IRestResponse response = client.Execute(request);
				string htmlContent = response.Content;

				{
					var matches = Regex.Matches(htmlContent, "\"spherical_hd_src\":(\"[^\"]+\")");
					if (matches.Count > 0)
					{
						var g1 = matches[0].Groups[1].Captures[0];
						string videoFile = JsonConvert.DeserializeObject<string>(g1.Value);
						return videoFile;
					}
				}
				{
					var matches = Regex.Matches(htmlContent, "\"spherical_sd_src\":(\"[^\"]+\")");
					if (matches.Count > 0)
					{
						var g1 = matches[0].Groups[1].Captures[0];
						string videoFile = JsonConvert.DeserializeObject<string>(g1.Value);
						return videoFile;
					}
				}
			}
			catch (Exception) { }

			return "";			
		}

		public static string ParseYoutube(Uri uri)
		{
			return "";
		}

		public static string ParseVrideo(Uri uri)
		{
			try {
				{
					string video4k = @"http://cdn2.vrideo.com/prod_videos/v1/" + uri.Segments.Last() + "_4k_full.mp4";
					RestClient client = new RestClient(video4k);
					IRestRequest request = new RestRequest(Method.HEAD);
					request.AddHeader("Accept", "text/html");
					IRestResponse response = client.Execute(request);
					if (response.StatusCode == System.Net.HttpStatusCode.OK)
						return video4k;
				}

				{
					string video2k = @"http://cdn2.vrideo.com/prod_videos/v1/" + uri.Segments.Last() + "_2k_full.mp4";
					RestClient client = new RestClient(video2k);
					IRestRequest request = new RestRequest(Method.HEAD);
					request.AddHeader("Accept", "text/html");
					IRestResponse response = client.Execute(request);
					if (response.StatusCode == System.Net.HttpStatusCode.OK)
						return video2k;
				}
				{
					string video1080p = @"http://cdn2.vrideo.com/prod_videos/v1/" + uri.Segments.Last() + "_1080p_full.mp4";
					RestClient client = new RestClient(video1080p);
					IRestRequest request = new RestRequest(Method.HEAD);
					request.AddHeader("Accept", "text/html");
					IRestResponse response = client.Execute(request);
					if (response.StatusCode == System.Net.HttpStatusCode.OK)
						return video1080p;
				}
			} catch(Exception) { }
			return "";
			//return @"http://cdn2.vrideo.com/prod_videos/v1/"+uri.Segments.Last()+"_4k_full.mp4";
		}

		public static string ParsePornhub(Uri uri)
		{
			//pornhub /player_quality_720p\s*=\s*'([^']+)'/.exec(document.body.innerHTML)[1]

			HtmlDocument document = DownloadDocument(uri);
			try
			{
				{
					var match = Regex.Match(document.DocumentNode.InnerHtml, @"player_quality_1080p\s*=\s*'([^']+)'");

					if (match.Captures.Count > 0)
					{
						return match.Groups[1].Captures[0].Value;
					}
				}

				{
					var match = Regex.Match(document.DocumentNode.InnerHtml, @"player_quality_720p\s*=\s*'([^']+)'");

					if (match.Captures.Count > 0)
					{
						return match.Groups[1].Captures[0].Value;
					}
				}

				{
					var match = Regex.Match(document.DocumentNode.InnerHtml, @"player_quality_480p\s*=\s*'([^']+)'");

					if (match.Captures.Count > 0)
					{
						return match.Groups[1].Captures[0].Value;
					}
				}

				{
					var match = Regex.Match(document.DocumentNode.InnerHtml, @"player_quality_240p\s*=\s*'([^']+)'");

					if (match.Captures.Count > 0)
					{
						return match.Groups[1].Captures[0].Value;
					}
				}

				{
					var match = Regex.Match(document.DocumentNode.InnerHtml, @"player_quality_180p\s*=\s*'([^']+)'");

					if (match.Captures.Count > 0)
					{
						return match.Groups[1].Captures[0].Value;
					}
				}

			}
			catch (Exception) { }
			return "";
		}

		public static string ParseUrl(Uri uri)
		{
			return uri.ToString();
		}

		public static string ParseLittlStar(Uri uri)
		{
			try
			{
				HtmlDocument document = DownloadDocument(uri);

				var node = document.DocumentNode.Descendants().Where(n => n.Name == "a" && n.GetAttributeValue("class", "").Contains("download")).First().GetAttributeValue("href", "");

				return node;
			}
			catch (Exception) { }

			return "";
		}

		public static MediaDecoder.ProjectionMode GetServiceProjection(Uri uri)
		{
			return GetServiceProjection(DetectService(uri));
		}

		public static MediaDecoder.ProjectionMode GetServiceProjection(Service service)
		{
			switch(service)
			{
				case Service.Facebook: return MediaDecoder.ProjectionMode.CubeFacebook;
				default: return MediaDecoder.ProjectionMode.Sphere; break;
			}
		}
	}
}