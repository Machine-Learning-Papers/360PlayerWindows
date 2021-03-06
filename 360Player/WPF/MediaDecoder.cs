﻿using Bivrost.Bivrost360Player.Streaming;
using Bivrost.Log;
using RestSharp;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.MediaFoundation;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace Bivrost.Bivrost360Player
{

	public enum VideoMode
	{
		Autodetect,
		Mono,
		SideBySide,
		TopBottom,
		SideBySideReversed,
		TopBottomReversed
	}


	public enum ProjectionMode
	{
		Autodetect,
		Sphere, // Equirectangular 360x180
		CubeFacebook,
		Dome    // Equirectangular 180x180
	}


	public class MediaDecoder
	{

		private Logger log = new Logger("MediaDecoder");

		public class Error
		{
			public long major;
			public int minor;
		}

		public Error LastError;

		private MediaEngine _mediaEngine;
		private MediaEngineEx _mediaEngineEx;
		private object criticalSection = new object();
		private bool waitForFormatChange = false;
		private bool formatChangePending = false;

		private long ts;
		private bool manualRender = false;

		private Texture2D _textureL;
		private Texture2D _textureR;
		protected Texture2D TextureL
		{
			get { return _textureL; }
			private set { _textureL = value; }
		}
		protected Texture2D TextureR
		{
			get { return _textureR; }
			private set { _textureR = value; }
		}

		public bool IsPlaying => isPlaying || IsDisplayingStaticContent;
		private bool isPlaying;
		public bool IsPaused {
            get
            {
				if (IsDisplayingStaticContent) return true;

                if (!_initialized) return false;

                if (_mediaEngineEx == null) return false;

                try
                {
                    return _mediaEngineEx.IsPaused;
                }
                catch(NullReferenceException)
                {
                    return false;
                }
            }
        }


		/// <summary>
		/// True if a panorama is displayed, false if a video or nothing at all is being played.
		/// </summary>
		public bool IsDisplayingStaticContent => bitmap != null;


		/// <summary>
		/// Used instead of mediaEngine to show non-animated content.
		/// Should contain the decoded png or jpeg (stored, so the textures can be recreated with other stereoscopy values)
		/// </summary>
		private Bitmap bitmap = null;


		private bool _loop = false;
		public bool Loop
		{
			get { return _loop; }
			set {
				_loop = value;
				if (_initialized) _mediaEngineEx.Loop = _loop;
			}
		}

		public bool IsEnded
		{
			get
			{
				lock (criticalSection)
				{
					return _initialized ? (bool)_mediaEngineEx.IsEnded : false;
				}
			}
		}

		//public int BufferLevel
		//{
		//    get
		//    {
		//        return _mediaEngineEx.
		//    }
		//}

		public int formatCounter = 0;

		public bool Ready { get; private set; }

		public double Duration { get; protected set; } = -1;  //  _mediaEngineEx.Duration; 

		public double CurrentPosition { get; protected set; } = 0; // _mediaEngineEx.CurrentTime; 
		public bool Initialized { get { return _initialized; } }
		private VideoMode LoadedStereoMode { get; set; } = VideoMode.Autodetect;

		private VideoMode _currentMode;
		public VideoMode CurrentMode
		{
			get { return _currentMode; }
			private set
			{
				if (_currentMode == value) return;

				_currentMode = value;
				log.Info($"Stereoscopy = {value}");
			}
		}


		private ProjectionMode _projection = ProjectionMode.Autodetect;
		public ProjectionMode Projection
		{
			get => _projection;
			set
			{
				_projection = value;
				OnContentChanged?.Invoke(); // no need for a critical section
			}
		}


		public ServiceResult CurrentServiceResult
		{
			get;
			protected set;
		}


		private bool _initialized = false;
		private bool _rendering = false;
		private ManualResetEvent waitForRenderingEnd = new ManualResetEvent(false);

		private static MediaDecoder _instance = null;
		public static MediaDecoder Instance { get { return MediaDecoder._instance; } }
		public static event Action<MediaDecoder> OnInstantiated;

		private SharpDX.Direct3D11.Device _device;
		private Factory _factory;
		private FeatureLevel[] _levels = new FeatureLevel[] { FeatureLevel.Level_10_0 };
		private DXGIDeviceManager _dxgiManager;

		public event Action OnPlay = delegate { };
		public event Action<double> OnReady = delegate { };
		public event Action OnStop = delegate { };
		public event Action OnEnded = delegate { };
		public event Action<Error> OnError = delegate { };
		public event Action OnAbort = delegate { };
		public event Action<double> OnTimeUpdate = delegate { };
		public event Action OnBufferingStarted = delegate { };
		public event Action OnBufferingEnded = delegate { };
		public event Action<double> OnProgress = delegate { };

		[Obsolete]
		public event Action<Texture2D, Texture2D> OnFormatChanged = delegate { };


		#region new file being played reporting

		/// <summary>
		/// Delegate for the OnNewFileIsPlaying event
		/// </summary>
		/// <param name="service">ServiceResult is of the media file playe.d</param>
		/// <param name="concreteFile">The file name (or URI) directly used - the service result might contain many alternatives.</param>
		/// <param name="duration">The media length. Unreliable for some streams, -1 for images</param>
		public delegate void NewFileIsPlayingDelegate(ServiceResult service, string concreteFile, double duration);

		/// <summary>
		/// Event played once after a video or image has been loaded and will be immediately displayed.
		/// The opposite of this is the OnStop event.
		/// </summary>
		public event NewFileIsPlayingDelegate OnNewFileIsPlaying;


		/// <summary>
		/// Flag used to mark that this streamingResult was already reported
		/// </summary>
		private bool _newFileIsPlayingReported = false; 


		protected void NewFileIsPlaying(ServiceResult sr, string concreteFile, double duration)
		{
			if (_newFileIsPlayingReported) return;
			_newFileIsPlayingReported = true;
			OnNewFileIsPlaying?.Invoke(sr, concreteFile, duration);
		}
		#endregion


		private bool textureReleased = true;
		public bool TextureReleased { get { return textureReleased; } }


		public MediaDecoder()
		{
			isPlaying = false;
			Ready = false;
			MediaDecoder._instance = this;
			if (MediaDecoder.OnInstantiated != null)
				MediaDecoder.OnInstantiated(this);
			_initialized = false;

			_factory = new SharpDX.DXGI.Factory();
			_device = new SharpDX.Direct3D11.Device(SharpDX.Direct3D.DriverType.Hardware, DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport, _levels);

			//SharpDX.DXGI.Device1 dxdevice = _device.QueryInterface<SharpDX.DXGI.Device1>();
			//MessageBox.Show(dxdevice.Adapter.Description.Description);
			//dxdevice.Dispose();

			DeviceMultithread mt = _device.QueryInterface<DeviceMultithread>();
			mt.SetMultithreadProtected(true);

			using (SharpDX.DXGI.Device1 dxgiDevice = _device.QueryInterface<SharpDX.DXGI.Device1>())
			{
				dxgiDevice.MaximumFrameLatency = 1;
			}

			_dxgiManager = new DXGIDeviceManager();

			//MFTEnumEx 
			//var category = SharpDX.MediaFoundation.TransformCategoryGuids.VideoDecoder;
			//var flags = SharpDX.MediaFoundation.TransformEnumFlag.Hardware | TransformEnumFlag.Localmft | TransformEnumFlag.SortAndFilter;
			//var typeInfo = new SharpDX.MediaFoundation.TRegisterTypeInformation();
			//typeInfo.GuidMajorType = MediaTypeGuids.Video;
			//typeInfo.GuidSubtype = VideoFormatGuids.H264;

			//Guid[] guids = new Guid[50];
			//int costamref;
			//MediaFactory.TEnum(category, (int)flags, null, null, null, guids, out costamref);
			//;			
		}


		public static string[] SupportedVideoFileExtensions => new string[] { "mp4", "m4v", "mov", "avi", "wmv" };
		public static string[] SupportedImageFileExtensions => new string[] { "png", "jpg", "jpeg", "png", "jpg", "jpeg" };
		public static string[] SupportedFileExtensions => SupportedVideoFileExtensions.Concat(SupportedImageFileExtensions).ToArray();


		public static bool CheckExtension(string extension)
		{
			string e = extension.TrimStart(new char[] { '.' });
			return SupportedVideoFileExtensions.Contains(e) || SupportedImageFileExtensions.Contains(e);
		}

		public static string ExtensionsFilter()
		{
			var allExtensions = string.Join("; ", SupportedFileExtensions.ToList().ConvertAll(ext => $"*.{ext}"));
			var videoExtensions = string.Join("; ", SupportedVideoFileExtensions.ToList().ConvertAll(ext => $"*.{ext}"));
			var imageExtensions = string.Join("; ", SupportedImageFileExtensions.ToList().ConvertAll(ext => $"*.{ext}"));

			return $"All supported formats|{allExtensions}|Video ({videoExtensions})|{videoExtensions}|Panorama ({imageExtensions})|{imageExtensions}";
		}

		public void Init()
		{
			LastError = null;
			criticalSection = new object();
			lock(criticalSection)
			{
				if (_initialized) return;

				MediaManager.Startup();
				var mediaEngineFactory = new MediaEngineClassFactory();

				_dxgiManager.ResetDevice(_device);

				MediaEngineAttributes attr = new MediaEngineAttributes();
				attr.VideoOutputFormat = (int)SharpDX.DXGI.Format.B8G8R8A8_UNorm;
				attr.DxgiManager = _dxgiManager;
				//attr.Set(MediaTypeAttributeKeys.TransferFunction.Guid, VideoTransferFunction.Func10);

				_mediaEngine = new MediaEngine(mediaEngineFactory, attr, MediaEngineCreateFlags.None);

				_mediaEngine.PlaybackEvent += (playEvent, param1, param2) =>
				{
					switch (playEvent)
					{
						case MediaEngineEvent.CanPlay:
							Console.WriteLine(string.Format("CAN PLAY {0}, {1}", param1, param2));
							Ready = true;
							Duration = _mediaEngineEx.Duration;
							OnReady(Duration);
							if (CurrentServiceResult == null)
								log.Error("Media playback aborted just after play?");
							else
								NewFileIsPlaying(CurrentServiceResult, CurrentServiceResult.BestSupportedStream, Duration);
							break;

						case MediaEngineEvent.TimeUpdate:
							CurrentPosition = _mediaEngineEx.CurrentTime;
							OnTimeUpdate(_mediaEngineEx.CurrentTime);
							break;

						case MediaEngineEvent.Error:
							Console.WriteLine(string.Format("ERROR {0}, {1}", param1, param2));
							Console.WriteLine(((MediaEngineErr)param1).ToString());
							LastError = new Error() { major = param1, minor = param2 };

							Stop(true);
							OnError(LastError);
							break;

						case MediaEngineEvent.Abort:
							Console.WriteLine(string.Format("ABORT {0}, {1}", param1, param2));
							OnAbort();
							Stop();
							break;

						case MediaEngineEvent.Ended:
                            Console.WriteLine(string.Format("ENDED {0}, {1}", param1, param2));
							OnEnded();
							break;
                        case MediaEngineEvent.BufferingStarted:
                            OnBufferingStarted();
                            break;
                        case MediaEngineEvent.BufferingEnded:
                            OnBufferingEnded();
                            break;
                        case MediaEngineEvent.FormatChange:
							Console.WriteLine("[!!!] FormatChange " + formatCounter++);
							formatChangePending = true;
							break;

                        case MediaEngineEvent.Playing:
                            OnPlay?.Invoke();
                            break;

                    }
                };

				_mediaEngineEx = _mediaEngine.QueryInterface<MediaEngineEx>();
				_mediaEngineEx.EnableWindowlessSwapchainMode(true);


				mediaEngineFactory.Dispose();
				_initialized = true;
			}
		}

		//private MediaSource CreateMediaSource(string sURL)
		//{
		//	SourceResolver sourceResolver = new SourceResolver();
		//	ComObject comObject;
		//	comObject = sourceResolver.CreateObjectFromURL(sURL, SourceResolverFlags.MediaSource | SourceResolverFlags.ContentDoesNotHaveToMatchExtensionOrMimeType);
		//	return comObject.QueryInterface<MediaSource>();
		//}

		//public static Guid ToGuid(long value)
		//{
		//	byte[] guidData = new byte[16];
		//	Array.Copy(BitConverter.GetBytes(value), guidData, 8);
		//	return new Guid(guidData);
		//}


		public Texture2D CreateTexture(SharpDX.Direct3D11.Device _device, int width, int height)
		{
			Texture2DDescription frameTextureDescription = new Texture2DDescription()
			{
				Width = width,
				Height = height,
				MipLevels = 1,
				ArraySize = 1,
				Format = Format.B8G8R8X8_UNorm,
				Usage = ResourceUsage.Default,
				SampleDescription = new SampleDescription(1, 0),
				BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
				CpuAccessFlags = CpuAccessFlags.None,
				OptionFlags = ResourceOptionFlags.Shared
			};

			var tex = new Texture2D(_device, frameTextureDescription);

			//tex.Disposing += (s, e) => log.Info("Disposing");
			//tex.Disposed += (s, e) => log.Info("Disposed"); ;

			return tex;
		}

		public Texture2D CreateTexture(SharpDX.Direct3D11.Device _device, int width, int height, DataRectangle dataRect)
		{
			Texture2DDescription frameTextureDescription = new Texture2DDescription()
			{
				Width = width,
				Height = height,
				MipLevels = 1,
				ArraySize = 1,
				Format = Format.B8G8R8X8_UNorm,
				Usage = ResourceUsage.Default,
				SampleDescription = new SampleDescription(1, 0),
				BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
				CpuAccessFlags = CpuAccessFlags.None,
				OptionFlags = ResourceOptionFlags.Shared
			};
			var tex = new Texture2D(_device, frameTextureDescription, dataRect);

			//tex.Disposing += (s, e) => log.Info("Disposing");
			//tex.Disposed += (s, e) => log.Info("Disposed");;

			return tex;
		}


		public class ClipCoords {

			public float w;
			public float h;
			public float l;
			public float t;

			public VideoNormalizedRect NormalizedSrcRect
			{
				get
				{
					return new VideoNormalizedRect()
					{
						Left = l,
						Top = t,
						Bottom = t + h,
						Right = l + w
					};
				}
			}

			internal SharpDX.Rectangle DstRect(int pxw, int pxh)
			{
				return new SharpDX.Rectangle(0, 0, Width(pxw), Height(pxh));
			}

			internal int Width(int pxw)
			{
				return (int)(w * pxw);
			}

			internal int Height(int pxh)
			{
				return (int)(h * pxh);
			}

			internal SharpDX.Rectangle SrcRect(int pxw, int pxh)
			{
				return new SharpDX.Rectangle((int)(l * pxw), (int)(t * pxh), (int)(w * pxw), (int)(h * pxh));
			}


			internal System.Drawing.Rectangle SrcRectSystemDrawing(int pxw, int pxh)
			{
				return new System.Drawing.Rectangle((int)(l * pxw), (int)(t * pxh), (int)(w * pxw), (int)(h * pxh));
			}

		}
		private static void GetTextureClipCoordinates(VideoMode mode, out ClipCoords texL, out ClipCoords texR)
		{
			switch (mode)
			{
				case VideoMode.Autodetect:
					throw new ArgumentException("Autodetect should not reach here");
				case VideoMode.Mono:
					texL = new ClipCoords() { w = 1, h = 1f, l = 0, t = 0 };
					texR = null;
					break;
				case VideoMode.SideBySide:
					texL = new ClipCoords() { w = 0.5f, h = 1, l = 0, t = 0 };
					texR = new ClipCoords() { w = 0.5f, h = 1, l = 0.5f, t = 0 };
					break;
				case VideoMode.SideBySideReversed:
					texL = new ClipCoords() { w = 0.5f, h = 1, l = 0.5f, t = 0 };
					texR = new ClipCoords() { w = 0.5f, h = 1, l = 0, t = 0 };
					break;
				case VideoMode.TopBottom:
					texL = new ClipCoords() { w = 1f, h = 0.5f, l = 0, t = 0 };
					texR = new ClipCoords() { w = 1f, h = 0.5f, l = 0, t = 0.5f };
					break;
				case VideoMode.TopBottomReversed:
					texL = new ClipCoords() { w = 1f, h = 0.5f, l = 0, t = 0.5f };
					texR = new ClipCoords() { w = 1f, h = 0.5f, l = 0, t = 0 };
					break;
				default:
					throw new Exception();
			}
		}


		public void Play()
		{
			lock(criticalSection)
			{
				if (isPlaying) return;
				if (IsDisplayingStaticContent) return;


				if (!_initialized) return;
				if (!Ready) return;

				var hasVideo = _mediaEngine.HasVideo();
				var hasAudio = _mediaEngine.HasAudio();

				if (hasVideo)
				{
					//SharpDX.Win32.Variant variant;
					//_mediaEngineEx.GetStreamAttribute(1, MediaTypeAttributeKeys.FrameSize.Guid, out variant);

					int w, h;
					_mediaEngineEx.GetNativeVideoSize(out w, out h);

					int cx, cy;
					_mediaEngineEx.GetVideoAspectRatio(out cx, out cy);
					var s3d = _mediaEngineEx.IsStereo3D;
					var sns = _mediaEngineEx.NumberOfStreams;
				}

				_mediaEngineEx.Play();
                ShellViewModel.SendEvent("moviePlaybackStarted");
                //_mediaEngineEx.Volume = 0.2;
                isPlaying = true;
			}

			Task t = Task.Factory.StartNew(() =>
			{
				_rendering = true;
				int w = -1, h = -1;
				while (_mediaEngine != null && isPlaying)
				{
					lock (criticalSection)
					{
						if(w < 0 || h < 0 || formatChangePending)
							_mediaEngineEx.GetNativeVideoSize(out w, out h);

						if(formatChangePending)
						{
							formatChangePending = false;
							CurrentMode = ParseStereoMode(LoadedStereoMode, w, h);
							ChangeFormat(CurrentMode, w, h);
						}

						if(!textureReleased)
						if (!_mediaEngine.IsPaused || manualRender)
						{
							manualRender = false;
							//waitForResize.WaitOne();
							if(formatCounter == 0)
								Console.WriteLine("[!!!] Render " + formatCounter++);
							long lastTs = ts;
							bool result = _mediaEngine.OnVideoStreamTick(out ts);
								if (!result || ts <= 0) Thread.Sleep(1);
							if(ts > 0)
							if (result && ts != lastTs)
							{
								//Duration = _mediaEngineEx.Duration;
								//CurrentPosition = _mediaEngineEx.CurrentTime; 

								try {
									ClipCoords texL;
									ClipCoords texR;
									GetTextureClipCoordinates(CurrentMode, out texL, out texR);

									_mediaEngine.TransferVideoFrame(TextureL, texL.NormalizedSrcRect, texL.DstRect(w,h), null);
									if(texR != null)
										_mediaEngine.TransferVideoFrame(TextureR, texR.NormalizedSrcRect, texR.DstRect(w,h), null);
								}
								catch (Exception exc)
								{
									log.Error(exc, "Playback exception");
								}
							}
						} else Thread.Sleep(1);
					}
				}

				waitForRenderingEnd.Set();
				_rendering = false;
			});
			
		}

		private VideoMode ParseStereoMode(VideoMode setStereoMode, float w, float h)
		{
			if (setStereoMode == VideoMode.Autodetect)
			{
				float videoAspect = w/h;
				var mode = (videoAspect < 1.3) ? VideoMode.TopBottom : VideoMode.Mono;
				log.Info($"Autodetected stereoscopy={mode} ({w}x{h}, aspect={videoAspect})");
				return mode;
			}
			else
			{
				return setStereoMode;
			}
		}


		/// <summary>
		/// Called by a IContentUpdatableFromMediaEngine when it wants
		/// to have their content updated
		/// </summary>
		/// <param name="obj"></param>
		/// <returns>true if content request was successful, false if media pipeline was not yet initialized.</returns>
		public bool ContentRequested(IContentUpdatableFromMediaEngine obj)
		{
			lock (criticalSection)
			{
				if(contentChangeDelegate == null)
				{
					//log.Error("ContentRequested without a delegate?");
					return false;
				}

				contentChangeDelegate(obj);
				var proj = Projection;
				if (proj == ProjectionMode.Autodetect)
					proj = ProjectionMode.Sphere;
				obj.SetProjection(proj);
			}

			return true;
		}

		/// <summary>
		/// When content changes (media, format, stereoscopy etc. changed), this event is called.
		/// Objects implementing IContentUpdatableFromMediaEngine should react and call ContentRequested.
		/// </summary>
		public event Action OnContentChanged = delegate { };


		Action<IContentUpdatableFromMediaEngine> contentChangeDelegate = null;


		/// <summary>
		/// Mark content as changed and notify whoever listens.
		/// </summary>
		/// <param name="contentChangeDelegate"></param>
		private void ContentChanged(Action<IContentUpdatableFromMediaEngine> contentChangeDelegate)
		{
			lock (criticalSection)
			{
				this.contentChangeDelegate = contentChangeDelegate;
				OnContentChanged?.Invoke();
			}
		}


		/// <summary>
		/// Should be executed in locked context
		/// </summary>
		/// <param name="w"></param>
		/// <param name="h"></param>
		private void ChangeFormat(VideoMode mode, int w, int h)
		{
			Texture2D tempL = TextureL;
			Texture2D tempR = TextureR;

			textureReleased = true;

			ClipCoords texL, texR;
			GetTextureClipCoordinates(mode, out texL, out texR);
			TextureL = CreateTexture(_device, texL.Width(w), texL.Height(h));
			TextureR = (texR != null) ? CreateTexture(_device, texR.Width(w), texR.Height(h)) : null;

			textureReleased = false;

			//OnReleaseTexture();
			OnFormatChanged(TextureL, TextureR);
			if (waitForFormatChange) waitForFormatChange = false;


			ContentChanged(r => r.ReceiveTextures(TextureL, TextureR));

			tempL?.Dispose();
			tempR?.Dispose();
		}


		public void Pause()
		{
			if (IsDisplayingStaticContent) return;
			if(isPlaying)
			{
				lock (criticalSection)
				{
					if (!_mediaEngine.IsPaused)
						_mediaEngine.Pause();
				}
			}
		}

		public void Unpause()
		{
			if (IsDisplayingStaticContent) return;
			if (isPlaying)
			{
				lock (criticalSection)
				{
					if (_mediaEngine.IsPaused)
						_mediaEngine.Play();
				}
			}
		}

		public void TogglePause()
		{
			if (IsDisplayingStaticContent) return;
			if (isPlaying)
			{
				if (IsPaused) Unpause();
				else Pause();
			}
		}

		public void SetVolume(double volume)
		{
            //if(IsPlaying)
            //{
            if (_mediaEngine != null)
            {
                try
                {
                    if(!_mediaEngine.IsDisposed)
                        _mediaEngine.Volume = volume;
                }
                catch (Exception) { }
            }
			//}
		}

		public void Seek(double time)
		{
			if (IsDisplayingStaticContent) return;
			if (isPlaying)
			{
				lock (criticalSection)
				{	
					if (!_mediaEngineEx.IsSeeking)
					{
						_mediaEngineEx.CurrentTime = time;
                        ShellViewModel.SendEvent("movieSeek", time);
                        if (IsPaused)
						{
							manualRender = true;
						}
					}
				}
			}
		}
		
		public void Stop(bool force = false)
		{
			textureReleased = true;
			//OnReleaseTexture();			

			if (!force && !IsDisplayingStaticContent)
			{
				if (!_initialized) return;
				if (!isPlaying) return;
			}

			waitForRenderingEnd.Reset();
			isPlaying = false;
			
			if (_rendering) {

				waitForRenderingEnd.WaitOne(1000);
			}

			Ready = false;

			lock (criticalSection)
			{
				if (_mediaEngineEx != null)
				{
					try
					{
						_mediaEngineEx.Shutdown();
						_mediaEngineEx.Dispose();
						_mediaEngine.Dispose();
					}
					catch (Exception e)
					{
						;
					}
				}

				bitmap?.Dispose();
				bitmap = null;

				TextureL?.Dispose();
				TextureR?.Dispose();

				TextureL = null;
				TextureR = null;

				//CurrentServiceResult = null;
				//Projection = ProjectionMode.Autodetect;
				//LoadedStereoMode = VideoMode.Autodetect;
				_newFileIsPlayingReported = false;

				ContentChanged(r => r.ClearContent());

				_initialized = false;	
			}

            OnStop?.Invoke();
        }

		//private string _fileName;
  //      public string FileName { get { return _fileName; } }

		public void LoadMedia(ServiceResult serviceResult)
		{
			CurrentServiceResult = serviceResult;
			Projection = serviceResult.projection;
			LoadedStereoMode = serviceResult.stereoscopy;

			string fileName = serviceResult.BestSupportedStream;

			Console.WriteLine("Load media: " + fileName);

			textureReleased = true;
			waitForFormatChange = true;
			Stop();
			while (_initialized == true)
			{
				Console.WriteLine("Cannot init when initialized");
				Stop(true);
				Thread.Sleep(5);
			}

			bitmap?.Dispose();
			bitmap = null;

			// reset the event
			_newFileIsPlayingReported = false;

			switch (serviceResult.contentType)
			{
				case ServiceResult.ContentType.video:
					Init();
					
					formatCounter = 0;
					textureReleased = true;
					waitForFormatChange = true;
					_mediaEngineEx.Source = fileName;
					_mediaEngineEx.Preload = MediaEnginePreload.Automatic;
					_mediaEngineEx.Load();
					break;

				case ServiceResult.ContentType.image:
					Task.Factory.StartNew(() => 
					{
						byte[] contentSource;

						if (fileName.StartsWith("http://", StringComparison.InvariantCultureIgnoreCase) || fileName.StartsWith("https://", StringComparison.InvariantCultureIgnoreCase))
						{
							RestClient client = new RestClient(fileName);
							IRestRequest request = new RestRequest(Method.GET);
							IRestResponse response = client.Execute(request);

							log.Info("Loading remote image file via HTTP: " + fileName);

							if ((int)response.StatusCode < 200 || (int)response.StatusCode >= 400) {
								log.Error($"Bad HTTP status: {response.StatusCode} ({(int)response.StatusCode})");
								OnError(new Error() { major = (long)MediaEngineErr.Network });
								return;
							}

							if (response.ErrorException != null)
							{
								log.Error(response.ErrorException, "Error while downloading");
								OnError(new Error() { major = (long)MediaEngineErr.Network });
								return;
							}

							contentSource = response.RawBytes;
						}
						else
						{
							log.Info("Loading local image file: " + fileName);
							try
							{
								contentSource = File.ReadAllBytes(fileName);
							}
							catch(Exception e)
							{
								log.Error(e, "Error while loading from disk");
								OnError(new Error() { major = (long)MediaEngineErr.SourceNotSupported });
								return;
							}
						}

						try
						{
							lock (criticalSection)
							{
								bitmap?.Dispose();
								using (var stream = new MemoryStream(contentSource))
								{
									bitmap = new Bitmap(stream);
								}
							}
						}
						catch(Exception e)
						{
							log.Error(e, "Error while decoding");
							OnError(new Error() { major = (long)MediaEngineErr.SourceNotSupported });
							return;
						}

						ContentChanged(renderer =>
						{
							if(bitmap == null)
							{
								renderer.ClearContent();
								log.Error("Content changed while loading, aborted.");
								return;
							}

							var w = bitmap.Width;
							var h = bitmap.Height;
							log.Info($"Loaded image of size {w}x{h} from {fileName}");
							CurrentMode = ParseStereoMode(LoadedStereoMode, w, h);

							ClipCoords texL, texR;
							GetTextureClipCoordinates(CurrentMode, out texL, out texR);

							renderer.ReceiveBitmap(bitmap, texL, texR);
						});

						OnReady?.Invoke(-1);

						NewFileIsPlaying(serviceResult, fileName, -1);
					});


					break;
			}
		}


		//public MediaEngine Engine { get { return this._mediaEngine; } }


		public void Shutdown()
		{
			MediaManager.Shutdown();
			_dxgiManager.Dispose();
			_factory.Dispose();
			_device.Dispose();
		}

	}
}

