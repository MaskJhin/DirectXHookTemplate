﻿using EasyHook;
using InjectionDll.Interface;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.Direct3D12;
using SharpDX.DXGI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace InjectionDll.DxHook
{
    enum D3D11DeviceVTbl : short
    {
        // IUnknown
        QueryInterface = 0,
        AddRef = 1,
        Release = 2,

        // ID3D11Device
        CreateBuffer = 3,
        CreateTexture1D = 4,
        CreateTexture2D = 5,
        CreateTexture3D = 6,
        CreateShaderResourceView = 7,
        CreateUnorderedAccessView = 8,
        CreateRenderTargetView = 9,
        CreateDepthStencilView = 10,
        CreateInputLayout = 11,
        CreateVertexShader = 12,
        CreateGeometryShader = 13,
        CreateGeometryShaderWithStreamOutput = 14,
        CreatePixelShader = 15,
        CreateHullShader = 16,
        CreateDomainShader = 17,
        CreateComputeShader = 18,
        CreateClassLinkage = 19,
        CreateBlendState = 20,
        CreateDepthStencilState = 21,
        CreateRasterizerState = 22,
        CreateSamplerState = 23,
        CreateQuery = 24,
        CreatePredicate = 25,
        CreateCounter = 26,
        CreateDeferredContext = 27,
        OpenSharedResource = 28,
        CheckFormatSupport = 29,
        CheckMultisampleQualityLevels = 30,
        CheckCounterInfo = 31,
        CheckCounter = 32,
        CheckFeatureSupport = 33,
        GetPrivateData = 34,
        SetPrivateData = 35,
        SetPrivateDataInterface = 36,
        GetFeatureLevel = 37,
        GetCreationFlags = 38,
        GetDeviceRemovedReason = 39,
        GetImmediateContext = 40,
        SetExceptionMode = 41,
        GetExceptionMode = 42,
    }

    internal class DXHookD3D11 : BaseDXHook
    {
        const int D3D11_DEVICE_METHOD_COUNT = 43;

        public DXHookD3D11(InterfaceDll ssInterface)
            : base(ssInterface)
        {
        }

        List<IntPtr> _d3d11VTblAddresses = null;
        List<IntPtr> _dxgiSwapChainVTblAddresses = null;

        LocalHook DXGISwapChain_PresentHook = null;
        LocalHook DXGISwapChain_ResizeTargetHook = null;

        protected override string HookName
        {
            get
            {
                return "DXHookD3D11";
            }
        }

        public override void Hook()
        {
            this.DebugMessage("Hook: Begin");
            if (_d3d11VTblAddresses == null)
            {
                _d3d11VTblAddresses = new List<IntPtr>();
                _dxgiSwapChainVTblAddresses = new List<IntPtr>();

                #region Get Device and SwapChain method addresses
                // Create temporary device + swapchain and determine method addresses
                SharpDX.Direct3D11.Device device;
                using (SharpDX.Windows.RenderForm renderForm = new SharpDX.Windows.RenderForm())
                {
                    this.DebugMessage("Hook: Before device creation");
                    SharpDX.Direct3D11.Device.CreateWithSwapChain(
                        DriverType.Hardware,
                        DeviceCreationFlags.None,
                        DXGI.CreateSwapChainDescription(renderForm.Handle),
                        out device,
                        out SwapChain swapChain);

                    if (device != null && swapChain != null)
                    {
                        this.DebugMessage("Hook: Device created");
                        using (device)
                        {
                            _d3d11VTblAddresses.AddRange(GetVTblAddresses(device.NativePointer, D3D11_DEVICE_METHOD_COUNT));

                            using (swapChain)
                            {
                                _dxgiSwapChainVTblAddresses.AddRange(GetVTblAddresses(swapChain.NativePointer, DXGI.DXGI_SWAPCHAIN_METHOD_COUNT));
                            }
                        }
                    }
                    else
                    {
                        this.DebugMessage("Hook: Device creation failed");
                    }
                }
                #endregion
            }

            // We will capture the backbuffer here
            DXGISwapChain_PresentHook = LocalHook.Create(
                _dxgiSwapChainVTblAddresses[(int)DXGI.DXGISwapChainVTbl.Present],
                new DXGISwapChain_PresentDelegate(PresentHook),
                this);

            // We will capture target/window resizes here
            DXGISwapChain_ResizeTargetHook = LocalHook.Create(
                _dxgiSwapChainVTblAddresses[(int)DXGI.DXGISwapChainVTbl.ResizeTarget],
                new DXGISwapChain_ResizeTargetDelegate(ResizeTargetHook),
                this);

            /*
             * Don't forget that all hooks will start deactivated...
             * The following ensures that all threads are intercepted:
             * Note: you must do this for each hook.
             */
            DXGISwapChain_PresentHook.ThreadACL.SetExclusiveACL(new Int32[1]);

            DXGISwapChain_ResizeTargetHook.ThreadACL.SetExclusiveACL(new Int32[1]);

            Hooks.Add(DXGISwapChain_PresentHook);
            Hooks.Add(DXGISwapChain_ResizeTargetHook);
        }

        public override void Cleanup()
        {
            try
            {
                if (DXGISwapChain_PresentHook != null)
                {
                    DXGISwapChain_PresentHook.Dispose();
                    DXGISwapChain_PresentHook = null;
                }
                if (DXGISwapChain_ResizeTargetHook != null)
                {
                    DXGISwapChain_ResizeTargetHook.Dispose();
                    DXGISwapChain_ResizeTargetHook = null;
                }

                if (_overlayEngine != null)
                {
                    _overlayEngine.Dispose();
                    _overlayEngine = null;
                }

                //this.Request = null;
            }
            catch
            {
            }
        }

        /// <summary>
        /// The IDXGISwapChain.Present function definition
        /// </summary>
        /// <param name="device"></param>
        /// <returns></returns>
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
        delegate int DXGISwapChain_PresentDelegate(IntPtr swapChainPtr, int syncInterval, /* int */ SharpDX.DXGI.PresentFlags flags);

        /// <summary>
        /// The IDXGISwapChain.ResizeTarget function definition
        /// </summary>
        /// <param name="device"></param>
        /// <returns></returns>
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
        delegate int DXGISwapChain_ResizeTargetDelegate(IntPtr swapChainPtr, ref ModeDescription newTargetParameters);

        /// <summary>
        /// Hooked to allow resizing a texture/surface that is reused. Currently not in use as we create the texture for each request
        /// to support different sizes each time (as we use DirectX to copy only the region we are after rather than the entire backbuffer)
        /// </summary>
        /// <param name="swapChainPtr"></param>
        /// <param name="newTargetParameters"></param>
        /// <returns></returns>
        int ResizeTargetHook(IntPtr swapChainPtr, ref ModeDescription newTargetParameters)
        {
            SwapChain swapChain = (SharpDX.DXGI.SwapChain)swapChainPtr;
            //using (SharpDX.DXGI.SwapChain swapChain = SharpDX.DXGI.SwapChain.FromPointer(swapChainPtr))
            {
                // This version creates a new texture for each request so there is nothing to resize.
                // IF the size of the texture is known each time, we could create it once, and then possibly need to resize it here

                // Dispose of overlay engine (so it will be recreated)
                if (_overlayEngine != null)
                {
                    _overlayEngine.Dispose();
                    _overlayEngine = null;
                }

                swapChain.ResizeTarget(ref newTargetParameters);
                return SharpDX.Result.Ok.Code;
            }
        }

        private SharpDX.Direct3D11.Device1 device;
        private SharpDX.Direct3D11.DeviceContext1 d3dContext;
        private SharpDX.Direct2D1.DeviceContext d2dContext;
        //private SwapChain1 swapChain;
        private SharpDX.Direct2D1.Bitmap1 d2dTarget;

        private SharpDX.Direct2D1.SolidColorBrush solidBrush;
        private SharpDX.Direct2D1.LinearGradientBrush linearGradientBrush;
        private SharpDX.Direct2D1.RadialGradientBrush radialGradientBrush;

        bool init = false;
        void InitText(SwapChain3 tempSwapChain)
        {
            init = true;
            device = tempSwapChain.GetDevice<SharpDX.Direct3D11.Device1>();
            d3dContext = device.ImmediateContext.QueryInterface<SharpDX.Direct3D11.DeviceContext1>();
            var texture2d = tempSwapChain.GetBackBuffer<Texture2D>(0);
            SharpDX.DXGI.Device2 dxgiDevice2 = device.QueryInterface<SharpDX.DXGI.Device2>();
            SharpDX.DXGI.Adapter dxgiAdapter = dxgiDevice2.Adapter;
            SharpDX.DXGI.Factory2 dxgiFactory2 = dxgiAdapter.GetParent<SharpDX.DXGI.Factory2>();

            SharpDX.Direct2D1.Device d2dDevice = new SharpDX.Direct2D1.Device(dxgiDevice2);
            d2dContext = new SharpDX.Direct2D1.DeviceContext(d2dDevice, SharpDX.Direct2D1.DeviceContextOptions.None);

            SharpDX.Direct2D1.BitmapProperties1 properties = new SharpDX.Direct2D1.BitmapProperties1(
                new SharpDX.Direct2D1.PixelFormat(SharpDX.DXGI.Format.B8G8R8A8_UNorm, 
                                                    SharpDX.Direct2D1.AlphaMode.Premultiplied),
                96, 96, SharpDX.Direct2D1.BitmapOptions.Target | SharpDX.Direct2D1.BitmapOptions.CannotDraw);

            Surface backBuffer = tempSwapChain.GetBackBuffer<Surface>(0);
            d2dTarget = new SharpDX.Direct2D1.Bitmap1(d2dContext, new Size2(800, 600), properties);

            solidBrush = new SharpDX.Direct2D1.SolidColorBrush(d2dContext, Color.Coral);

            // Create a linear gradient brush.
            // Note that the StartPoint and EndPoint values are set as absolute coordinates of the surface you are drawing to,
            // NOT the geometry we will apply the brush.
            linearGradientBrush = new SharpDX.Direct2D1.LinearGradientBrush(d2dContext, new SharpDX.Direct2D1.LinearGradientBrushProperties()
            {
                StartPoint = new Vector2(50, 0),
                EndPoint = new Vector2(450, 0),
            },
                new SharpDX.Direct2D1.GradientStopCollection(d2dContext, new SharpDX.Direct2D1.GradientStop[]
                    {
                        new SharpDX.Direct2D1.GradientStop()
                        {
                            Color = Color.Blue,
                            Position = 0,
                        },
                        new SharpDX.Direct2D1.GradientStop()
                        {
                            Color = Color.Green,
                            Position = 1,
                        }
                    }));

            SharpDX.Direct2D1.RadialGradientBrushProperties rgb = new SharpDX.Direct2D1.RadialGradientBrushProperties()
            {
                Center = new Vector2(250, 525),
                RadiusX = 100,
                RadiusY = 100,
            };
            // Create a radial gradient brush.
            // The center is specified in absolute coordinates, too.
            radialGradientBrush = new SharpDX.Direct2D1.RadialGradientBrush(d2dContext, ref rgb
            ,
                new SharpDX.Direct2D1.GradientStopCollection(d2dContext, new SharpDX.Direct2D1.GradientStop[]
                {
                        new SharpDX.Direct2D1.GradientStop()
                        {
                            Color = Color.Yellow,
                            Position = 0,
                        },
                        new SharpDX.Direct2D1.GradientStop()
                        {
                            Color = Color.Red,
                            Position = 1,
                        }
                }));
        }

        /// <summary>
        /// Our present hook that will grab a copy of the backbuffer when requested. Note: this supports multi-sampling (anti-aliasing)
        /// </summary>
        /// <param name="swapChainPtr"></param>
        /// <param name="syncInterval"></param>
        /// <param name="flags"></param>
        /// <returns>The HRESULT of the original method</returns>
        int PresentHook(IntPtr swapChainPtr, int syncInterval, SharpDX.DXGI.PresentFlags flags)
        {
            this.Frame();
            SwapChain3 swapChain = (SharpDX.DXGI.SwapChain3)swapChainPtr;
            //if (!init)
            //{
            //    InitText(swapChain);
            //}

            try
            {
                #region Screenshot Request
                //if (this.Request != null)
                //{
                //    this.DebugMessage("PresentHook: Request Start");
                //    DateTime startTime = DateTime.Now;
                //    using (Texture2D texture = Texture2D.FromSwapChain<Texture2D>(swapChain, 0))
                //    {
                //        #region Determine region to capture
                //        System.Drawing.Rectangle regionToCapture = new System.Drawing.Rectangle(0, 0, texture.Description.Width, texture.Description.Height);

                //        if (this.Request.RegionToCapture.Width > 0)
                //        {
                //            regionToCapture = this.Request.RegionToCapture;
                //        }
                //        #endregion
                        
                //        var theTexture = texture;

                //        // If texture is multisampled, then we can use ResolveSubresource to copy it into a non-multisampled texture
                //        Texture2D textureResolved = null;
                //        if (texture.Description.SampleDescription.Count > 1)
                //        {
                //            this.DebugMessage("PresentHook: resolving multi-sampled texture");
                //            // texture is multi-sampled, lets resolve it down to single sample
                //            textureResolved = new Texture2D(texture.Device, new Texture2DDescription()
                //            {
                //                CpuAccessFlags = CpuAccessFlags.None,
                //                Format = texture.Description.Format,
                //                Height = texture.Description.Height,
                //                Usage = ResourceUsage.Default,
                //                Width = texture.Description.Width,
                //                ArraySize = 1,
                //                SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0), // Ensure single sample
                //                BindFlags = BindFlags.None,
                //                MipLevels = 1,
                //                OptionFlags = texture.Description.OptionFlags
                //            });
                //            // Resolve into textureResolved
                //            texture.Device.ImmediateContext.ResolveSubresource(texture, 0, textureResolved, 0, texture.Description.Format);

                //            // Make "theTexture" be the resolved texture
                //            theTexture = textureResolved;
                //        }

                //        // Create destination texture
                //        Texture2D textureDest = new Texture2D(texture.Device, new Texture2DDescription()
                //        {
                //            CpuAccessFlags = CpuAccessFlags.None,// CpuAccessFlags.Write | CpuAccessFlags.Read,
                //            Format = SharpDX.DXGI.Format.R8G8B8A8_UNorm, // Supports BMP/PNG
                //            Height = regionToCapture.Height,
                //            Usage = ResourceUsage.Default,// ResourceUsage.Staging,
                //            Width = regionToCapture.Width,
                //            ArraySize = 1,//texture.Description.ArraySize,
                //            SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),// texture.Description.SampleDescription,
                //            BindFlags = BindFlags.None,
                //            MipLevels = 1,//texture.Description.MipLevels,
                //            OptionFlags = texture.Description.OptionFlags
                //        });
                        
                //        // Copy the subresource region, we are dealing with a flat 2D texture with no MipMapping, so 0 is the subresource index
                //        theTexture.Device.ImmediateContext.CopySubresourceRegion(theTexture, 0, new ResourceRegion()
                //        {
                //            Top = regionToCapture.Top,
                //            Bottom = regionToCapture.Bottom,
                //            Left = regionToCapture.Left,
                //            Right = regionToCapture.Right,
                //            Front = 0,
                //            Back = 1 // Must be 1 or only black will be copied
                //        }, textureDest, 0, 0, 0, 0);

                //        // Note: it would be possible to capture multiple frames and process them in a background thread

                //        // Copy to memory and send back to host process on a background thread so that we do not cause any delay in the rendering pipeline
                //        Guid requestId = this.Request.RequestId; // this.Request gets set to null, so copy the RequestId for use in the thread
                //        ThreadPool.QueueUserWorkItem(delegate
                //        {
                //            //FileStream fs = new FileStream(@"c:\temp\temp.bmp", FileMode.Create);
                //            //Texture2D.ToStream(testSubResourceCopy, ImageFileFormat.Bmp, fs);

                //            DateTime startCopyToSystemMemory = DateTime.Now;
                //            using (MemoryStream ms = new MemoryStream())
                //            {
                //                Texture2D.ToStream(textureDest.Device.ImmediateContext, textureDest, ImageFileFormat.Bmp, ms);
                //                ms.Position = 0;
                //                this.DebugMessage("PresentHook: Copy to System Memory time: " + (DateTime.Now - startCopyToSystemMemory).ToString());

                //                DateTime startSendResponse = DateTime.Now;
                //                ProcessCapture(ms, requestId);
                //                this.DebugMessage("PresentHook: Send response time: " + (DateTime.Now - startSendResponse).ToString());
                //            }

                //            // Free the textureDest as we no longer need it.
                //            textureDest.Dispose();
                //            textureDest = null;
                //            this.DebugMessage("PresentHook: Full Capture time: " + (DateTime.Now - startTime).ToString());
                //        });

                //        // Prevent the request from being processed a second time
                //        this.Request = null;

                //        // Make sure we free up the resolved texture if it was created
                //        if (textureResolved != null)
                //        {
                //            textureResolved.Dispose();
                //            textureResolved = null;
                //        }
                //    }
                //    this.DebugMessage("PresentHook: Copy BackBuffer time: " + (DateTime.Now - startTime).ToString());
                //    this.DebugMessage("PresentHook: Request End");
                //}
                #endregion

                #region Draw overlay (after screenshot so we don't capture overlay as well)
                if (this.Config.ShowOverlay)
                {
                    // Initialise Overlay Engine
                    if (_swapChainPointer != swapChain.NativePointer || _overlayEngine == null)
                    {
                        if (_overlayEngine != null)
                            _overlayEngine.Dispose();

                        _overlayEngine = new DX11.DXOverlayEngine();
                        _overlayEngine.Overlays.Add(new Common.Overlay
                        {
                            Elements =
                            {
                                //new Common.TextElement(new System.Drawing.Font("Times New Roman", 22)) { Text = "Test", Location = new System.Drawing.Point(200, 200), Color = System.Drawing.Color.Yellow, AntiAliased = false},
                                new Common.FramesPerSecond(new System.Drawing.Font("Arial", 12)) { Location = new System.Drawing.Point(5,5), Color = System.Drawing.Color.Red, AntiAliased = true },
                                //new Common.ProcessLoad(new System.Drawing.Font("Arial", 12)){ Location = new System.Drawing.Point(5,20), Color = System.Drawing.Color.Red, AntiAliased = true },
                                ProcessLoad
                            }
                        });
                        _overlayEngine.Initialise(swapChain);

                        _swapChainPointer = swapChain.NativePointer;
                    }
                    // Draw Overlay(s)
                    else if (_overlayEngine != null)
                    {
                        foreach (var overlay in _overlayEngine.Overlays)
                            overlay.Frame();
                        _overlayEngine.Draw();
                    }

                    //direct2DRenderTarget[frameIndex].BeginDraw();
                    //int time = Environment.TickCount % 10000;
                    //int t = time / 2000;
                    //float f = (time - (t * 2000)) / 2000.0F;
                    //textBrush.Color = Color4.Lerp(colors[t], colors[t + 1], f);
                    //direct2DRenderTarget[frameIndex].DrawText("Hello Text", textFormat, new SharpDX.Mathematics.Interop.RawRectangleF((float)Math.Sin(Environment.TickCount / 1000.0F) * 200 + 400, 10, 2000, 500), textBrush);
                    //direct2DRenderTarget[frameIndex].EndDraw();
                }
                #endregion
            }
            catch (Exception e)
            {
                // If there is an error we do not want to crash the hooked application, so swallow the exception
                this.DebugMessage("PresentHook: Exeception: " + e.GetType().FullName + ": " + e.ToString());
                //return unchecked((int)0x8000FFFF); //E_UNEXPECTED
            }

            // As always we need to call the original method, note that EasyHook has already repatched the original method
            // so calling it here will not cause an endless recursion to this function
            swapChain.Present(syncInterval, flags);
            return SharpDX.Result.Ok.Code;
        }

        DX11.DXOverlayEngine _overlayEngine;

        IntPtr _swapChainPointer = IntPtr.Zero;
    }
}
