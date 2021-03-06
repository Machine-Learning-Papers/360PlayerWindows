﻿using Bivrost.Log;
using Bivrost.Bivrost360Player.Streaming;
using System.Threading.Tasks;
using System.Windows;

namespace Bivrost.Bivrost360Player
{
	/// <summary>
	/// Interaction logic for OpenUrlView.xaml
	/// </summary>
	public partial class ServiceResultResolver : Window
	{
		private static Logger log = new Logger("ServiceResultResolver");


		protected ServiceResultResolver(Window owner)
		{
			Owner = owner;
			InitializeComponent();
		}

		public static ServiceResult DialogProcessURIBlocking(string uri, Window owner)
		{
			var window = new ServiceResultResolver(owner);

			ServiceResult result = null;
			bool closed = false;

			var task = Task.Run(() =>
			{
                //result = StreamingFactory.Instance.GetStreamingInfo(uri);
                result = ProcessURI(uri);
                if (closed)
					log.Info($"Streaming result resolving was cancelled before completion. Uri={uri}.");
				else
					window.Dispatcher.InvokeAsync(() => window.Close());
			});

			window.ShowDialog();
			closed = true;

			return result;
		}

		private void Cancel_Click(object sender, RoutedEventArgs e)
		{
			Close();
		}

		/// <summary>
		/// Processes an URI and returns a ServiceResult or null on failure
		/// Displays notifications on errors.
		/// </summary>
		/// <param name="uri"></param>
		/// <returns>ServiceResult with parsed streaming information or null</returns>
		protected static ServiceResult ProcessURI(string uri)
		{
			try
			{
				var sr = Streaming.StreamingFactory.Instance.GetStreamingInfo(uri);

				// no error, but nothing found (probably couldn't parse)
				if (sr == null)
					Logic.Notify("Url is not valid video or recognised streaming service address.");

				return sr;
			}
			catch (Streaming.StreamNotSupported exc)
			{
				log.Error(exc, "Streaming: video not supported: " + uri);
				Logic.Notify("Video not yet supported.");
			}
			catch (Streaming.StreamParsingFailed exc)
			{
				log.Error(exc, "Streaming: Parsing failed. Unable to open the video: " + uri);
				Logic.Notify("Parsing failed. Unable to open the video.");
			}
			catch (Streaming.StreamNetworkFailure exc)
			{
				log.Error(exc, "Streaming: Network/file failure. Unable to open the video: " + uri);
				Logic.Notify("This file is currently unavailable.");
			}
			catch (System.Exception exc)
			{
				log.Error(exc, "Streaming: media not supported: " + uri);
				Logic.Notify("Media not supported.");
			}

			return null;
		}
	}
}
