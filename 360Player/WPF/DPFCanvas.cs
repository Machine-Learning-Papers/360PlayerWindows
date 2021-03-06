﻿namespace Bivrost.Bivrost360Player
{
    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Media;
    using SharpDX;
    using SharpDX.Direct3D11;
    using SharpDX.DXGI;
    using Device = SharpDX.Direct3D11.Device;
    using SharpDX.Direct3D;
    using Bivrost.Bivrost360Player.WPF;
    using System.Threading;
    using System.Collections.Generic;
    using System.Collections.Concurrent;

    public partial class DPFCanvas : Image, ISceneHost
    {
        private Device Device;

        private Texture2D RenderTarget;
        private Texture2D DepthStencil;
        private RenderTargetView RenderTargetView;
        private DepthStencilView DepthStencilView;

        private Texture2D RenderTarget2;
        private Texture2D DepthStencil2;
        private RenderTargetView RenderTargetView2;
        private DepthStencilView DepthStencilView2;

        private DX11ImageSource D3DSurface;
        private Stopwatch RenderTimer;
        private IScene RenderScene;
        private bool SceneAttached;
        
		public Color4 ClearColor = SharpDX.Color.Black;

		//public Texture2D extTexture = null;

		private SharpDX.DXGI.Device1 dxdevice;

		public event Action NeedsReloading = delegate { };

        public CancellationToken token;
        public CancellationTokenSource cts;
        public object renderLock = new object();


        public DPFCanvas()
        {
            this.RenderTimer = new Stopwatch();
            this.Loaded += this.Window_Loaded;
            this.Unloaded += this.Window_Closing;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (DPFCanvas.IsInDesignMode)
                return;

			NeedsReloading += () => Caliburn.Micro.Execute.OnUIThread(() =>
				{
					this.StopRendering();
					this.EndD3D();
					this.StartD3D();
					this.StartRendering();
				});

			this.StartD3D();
			//this.StartRendering();
        }

		public void LateStartD3D()
		{
			
		}

        private void Window_Closing(object sender, RoutedEventArgs e)
        {
            if (DPFCanvas.IsInDesignMode)
                return;

            this.StopRendering();
            this.EndD3D();
        }

		public Device GetDevice() { return this.Device; }

        private void StartD3D()
        {
            //this.Device = new Device(DriverType.Hardware, DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport | DeviceCreationFlags.Debug, FeatureLevel.Level_11_0);
			this.Device = new Device(DriverType.Hardware, DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport, FeatureLevel.Level_10_0);

			this.dxdevice = Device.QueryInterface<SharpDX.DXGI.Device1>();
			//MessageBox.Show(dxdevice.Adapter.Description.Description);

			dxdevice.Disposing += (e, s) =>
			{
				Console.WriteLine("DISPOSING WPF D3D");
			};

			this.D3DSurface = new DX11ImageSource();
            this.D3DSurface.IsFrontBufferAvailableChanged += OnIsFrontBufferAvailableChanged;

            this.CreateAndBindTargets();

            this.Source = this.D3DSurface;
		}

        private void EndD3D()
        {
            if (this.RenderScene != null)
            {
                this.RenderScene.Detach();
                this.SceneAttached = false;
            }

            this.D3DSurface.IsFrontBufferAvailableChanged -= OnIsFrontBufferAvailableChanged;
            this.Source = null;

            Disposer.RemoveAndDispose(ref this.D3DSurface);

            Disposer.RemoveAndDispose(ref this.RenderTargetView);
            Disposer.RemoveAndDispose(ref this.DepthStencilView);
            Disposer.RemoveAndDispose(ref this.RenderTarget);
            Disposer.RemoveAndDispose(ref this.DepthStencil);

            Disposer.RemoveAndDispose(ref this.RenderTargetView2);
            Disposer.RemoveAndDispose(ref this.DepthStencilView2);
            Disposer.RemoveAndDispose(ref this.RenderTarget2);
            Disposer.RemoveAndDispose(ref this.DepthStencil2);

            Disposer.RemoveAndDispose(ref this.dxdevice);
            Disposer.RemoveAndDispose(ref this.Device);
        }

        public void CreateAndBindTargets()
        {
			this.D3DSurface.SetRenderTargetDX11(null);

			Disposer.RemoveAndDispose(ref this.RenderTargetView);
			Disposer.RemoveAndDispose(ref this.DepthStencilView);
			Disposer.RemoveAndDispose(ref this.RenderTarget);
			Disposer.RemoveAndDispose(ref this.DepthStencil);

            Disposer.RemoveAndDispose(ref this.RenderTargetView2);
            Disposer.RemoveAndDispose(ref this.DepthStencilView2);
            Disposer.RemoveAndDispose(ref this.RenderTarget2);
            Disposer.RemoveAndDispose(ref this.DepthStencil2);

            int width = (int)System.Windows.SystemParameters.PrimaryScreenWidth;
			int height = (int)System.Windows.SystemParameters.PrimaryScreenHeight;

			Texture2DDescription colordesc = new Texture2DDescription
			{
				BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
				Format = Format.B8G8R8A8_UNorm,
				Width = width,
				Height = height,
				MipLevels = 1,
				SampleDescription = new SampleDescription(1, 0),
				Usage = ResourceUsage.Default,
				OptionFlags = ResourceOptionFlags.Shared,
				CpuAccessFlags = CpuAccessFlags.None,
				ArraySize = 1
			};

			Texture2DDescription depthdesc = new Texture2DDescription
			{
				BindFlags = BindFlags.DepthStencil,
				Format = Format.D32_Float_S8X24_UInt,
				Width = width,
				Height = height,
				MipLevels = 1,
				SampleDescription = new SampleDescription(1, 0),
				Usage = ResourceUsage.Default,
				OptionFlags = ResourceOptionFlags.None,
				CpuAccessFlags = CpuAccessFlags.None,
				ArraySize = 1,
			};

			this.RenderTarget = new Texture2D(this.Device, colordesc);
			this.DepthStencil = new Texture2D(this.Device, depthdesc);
			this.RenderTargetView = new RenderTargetView(this.Device, this.RenderTarget);
			this.DepthStencilView = new DepthStencilView(this.Device, this.DepthStencil);

            this.RenderTarget2 = new Texture2D(this.Device, colordesc);
            this.DepthStencil2 = new Texture2D(this.Device, depthdesc);
            this.RenderTargetView2 = new RenderTargetView(this.Device, this.RenderTarget2);
            this.DepthStencilView2 = new DepthStencilView(this.Device, this.DepthStencil2);

            this.D3DSurface.SetRenderTargetDX11(this.RenderTarget);
        }

        public AutoResetEvent waitForFrame = new AutoResetEvent(false);
        public void RenderThread(object token)
        {
            var localToken = (CancellationToken)token;
            while(!localToken.IsCancellationRequested)
            {
                if(waitForFrame.WaitOne(20))
                    if(!localToken.IsCancellationRequested)
                    {
                        if (Monitor.TryEnter(renderLock, 16))
                        {
                            this.Render(this.RenderTimer.Elapsed);
                            Monitor.Exit(renderLock);
                        }
                    }
            }
        }

        public void StartRendering()
        {
            if (this.RenderTimer.IsRunning)
                return;

            if (!Device.IsDisposed)
            {
                Device.ImmediateContext.ClearRenderTargetView(RenderTargetView, Color4.Black);
                Device.ImmediateContext.ClearRenderTargetView(RenderTargetView2, Color4.Black);
            }


            cts = new CancellationTokenSource();
            token = cts.Token;
            (new Thread(new ParameterizedThreadStart(RenderThread)) { IsBackground = true }).Start(token);

            CompositionTarget.Rendering += OnRendering;
            this.RenderTimer.Start();
            //this.fpsWatch.Start();
        }

        public void StopRendering()
        {
            if (!this.RenderTimer.IsRunning)
                return;

            CompositionTarget.Rendering -= OnRendering;
            this.RenderTimer.Stop();
            //this.fpsWatch.Stop();
            cts.Cancel();
        }

		private bool odd = false;
        private TimeSpan lastRender;
        //private Stopwatch fpsWatch = new Stopwatch();
        //long frames = 0;

        private void OnRendering(object sender, EventArgs e)
        {
            //if (odd)
            //{
            //	odd = false;
            //	return;
            //}
            //else odd = true;

            //foreach(KeyValuePair<System.Windows.Input.Key, bool> kvp in KeyState)
            //{
            //    KeyState[kvp.Key] = System.Windows.Input.Keyboard.IsKeyDown(kvp.Key);
            //    if (KeyState[kvp.Key])
            //        ;
            //}

            RenderingEventArgs args = (RenderingEventArgs)e;
            if (this.lastRender != args.RenderingTime)
            {                
                if (!this.RenderTimer.IsRunning)
                    return;

                //this.Render(this.RenderTimer.Elapsed);
                

                if (Monitor.TryEnter(renderLock, 16))
                {
                    if (this.Device != null && !Device.IsDisposed && !RenderTarget.IsDisposed && !RenderTarget2.IsDisposed)
                    {
                        if (RenderTarget.IsDisposed)
                            ;
                        if (RenderTarget2.IsDisposed)
                            ;
                        this.Device.ImmediateContext.CopyResource(RenderTarget2, RenderTarget);
                    }
                    Monitor.Exit(renderLock);
                }
                    this.D3DSurface.InvalidateD3DImage();

                this.waitForFrame.Set();

                this.lastRender = args.RenderingTime;
            }
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            //this.CreateAndBindTargets();
            base.OnRenderSizeChanged(sizeInfo);
        }

        void Render(TimeSpan sceneTime)
        {
			try {
				SharpDX.Direct3D11.Device device = this.Device;
				if (device == null)
					return;

				if (device.IsDisposed)
				{
					NeedsReloading();
					return;
				}

				Texture2D renderTarget = this.RenderTarget2;
				if (renderTarget == null)
					return;

				int targetWidth = renderTarget.Description.Width;
				int targetHeight = renderTarget.Description.Height;

				device.ImmediateContext.OutputMerger.SetTargets(this.DepthStencilView2, this.RenderTargetView2);
				device.ImmediateContext.Rasterizer.SetViewport(new Viewport(0, 0, targetWidth, targetHeight, 0.0f, 1.0f));

				device.ImmediateContext.ClearRenderTargetView(this.RenderTargetView2, this.ClearColor);
				device.ImmediateContext.ClearDepthStencilView(this.DepthStencilView2, DepthStencilClearFlags.Depth | DepthStencilClearFlags.Stencil, 1.0f, 0);



				if (this.Scene != null)
				{
					if (!this.SceneAttached)
					{
						this.SceneAttached = true;
						this.RenderScene.Attach(this);
					}

					this.Scene.Update(this.RenderTimer.Elapsed);
					this.Scene.Render();
				}

				device.ImmediateContext.Flush();
			} catch(Exception exc)
			{
				Console.WriteLine("[EXC] " + exc.Message);
				Console.WriteLine("[EXC] " + exc.StackTrace);
				;
			}
        }

        private void OnIsFrontBufferAvailableChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // this fires when the screensaver kicks in, the machine goes into sleep or hibernate
            // and any other catastrophic losses of the d3d device from WPF's point of view
			if (this.D3DSurface.IsFrontBufferAvailable)
			{
				CreateAndBindTargets();
				this.StartRendering();
			}
			else
				this.StopRendering();
        }

        /// <summary>
        /// Gets a value indicating whether the control is in design mode
        /// (running in Blend or Visual Studio).
        /// </summary>
        public static bool IsInDesignMode
        {
            get
            {
                DependencyProperty prop = DesignerProperties.IsInDesignModeProperty;
                bool isDesignMode = (bool)DependencyPropertyDescriptor.FromProperty(prop, typeof(FrameworkElement)).Metadata.DefaultValue;
                return isDesignMode;
            }
        }

        public IScene Scene
        {
            get { return this.RenderScene; }
            set
            {
                if (ReferenceEquals(this.RenderScene, value))
                    return;

                if (this.RenderScene != null)
                    this.RenderScene.Detach();

                this.SceneAttached = false;
                this.RenderScene = value;
            }
        }

        SharpDX.Direct3D11.Device ISceneHost.Device
        {
            get { return this.Device; }
        }




		//public void SetVideoTexture(Texture2D tex)
		//{
		//	SharpDX.DXGI.Device1 dxgiDevice1 = Device.QueryInterface<SharpDX.DXGI.Device1>();
		//	var luid = dxgiDevice1.Adapter.Description.Luid;

		//	IntPtr hWnd = NativeApiWrapper.FindWindow(null, "bivrost");
		//	if (hWnd == IntPtr.Zero)
		//	{
		//		Console.WriteLine("Could not find window.");
		//		//return;
		//	}
		//	else
		//	{
		//		uint format = (uint)SharpDX.Direct3D9.Format.A8B8G8R8;
		//		IntPtr pSharedHandle = IntPtr.Zero;
		//		int hr = NativeApiWrapper.GetSharedSurface(hWnd, luid, 0, 0, ref format, out pSharedHandle, 0);

		//		RECT winRect;
		//		NativeApiWrapper.GetWindowRect(hWnd, out winRect);


		//		var tempResource = Device.OpenSharedResource<SharpDX.Direct3D11.Resource>(pSharedHandle);
		//		Texture2D sharedTex = tempResource.QueryInterface<Texture2D>();
		//		tempResource.Dispose();

		//		//Texture2D sharedTex = Device.OpenSharedResource<Texture2D>(resource.SharedHandle);

		//		//this.Scene.SetVideoTexture(sharedTex);
				
		//	}

		//	this.Scene.SetVideoTexture(tex);

		//}
	}
}
