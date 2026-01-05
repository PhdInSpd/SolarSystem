using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using Veldrid.SPIRV;
using PlcTools;
using System.Drawing;
using System.Drawing.Text;
using System.Drawing.Drawing2D;

namespace SolarSystem
{
    static class Body
    {
        public static readonly int Sun = 0;
    }

    struct SolarBody
    {
        public float Mass;
        public float Radius;
        public DeviceTexture? Texture;
        public RgbaFloat Color;
        public Vector2 Position;
        public Vector2 Velocity;
    }

    static class Vector2Extensions
    {
        public static Vector2 Rotate(this Vector2 position, float angle)
        {
            float cos = MathF.Cos(angle);
            float sin = MathF.Sin(angle);
            return new Vector2(
                position.X * cos - position.Y * sin,
                position.X * sin + position.Y * cos
            );
        }

        public static Vector2 ClockwisePerpendicular(this Vector2 position)
        {
            return position.Rotate(MathF.PI / 2.0f);
        }

        public static Vector2 CounterClockwisePerpendicular(this Vector2 position)
        {
            return position.Rotate(-MathF.PI / 2.0f);
        }

        public static Vector2 Unit(this Vector2 vector)
        {
            return Vector2.Normalize(vector);
        }
    }

    static class GravityForce
    {
        public static readonly float G = 35.00f;

        public static Vector2 TwoBodies(this SolarBody solarFrom, SolarBody solarTo)
        {
            Vector2 r = solarTo.Position - solarFrom.Position;
            Vector2 unitAccel = Vector2.Normalize(r);
            float gravityForce = G * solarFrom.Mass * solarTo.Mass / r.LengthSquared();
            return gravityForce * unitAccel;
        }

        public static Vector2 AllBodies(this SolarBody[] bodies, int from)
        {
            Vector2 totalGravityForce = Vector2.Zero;
            for (int to = 0; to < bodies.Length; to++)
            {
                if (from != to)
                {
                    Vector2 gravity = bodies[from].TwoBodies(bodies[to]);
                    totalGravityForce += gravity;
                }
            }
            return totalGravityForce;
        }
    }

    public class DeviceTexture : IDisposable
    {
        public Texture Texture { get; }
        public TextureView TextureView { get; }
        public uint Width { get; }
        public uint Height { get; }

        public DeviceTexture(GraphicsDevice device, uint width, uint height, RgbaFloat[] data)
        {
            Width = width;
            Height = height;

            Texture = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
                width, height, 1, 1,
                PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));

            TextureView = device.ResourceFactory.CreateTextureView(Texture);

            byte[] pixelData = new byte[width * height * 4];
            for (int i = 0; i < data.Length; i++)
            {
                pixelData[i * 4 + 0] = (byte)(data[i].R * 255);
                pixelData[i * 4 + 1] = (byte)(data[i].G * 255);
                pixelData[i * 4 + 2] = (byte)(data[i].B * 255);
                pixelData[i * 4 + 3] = (byte)(data[i].A * 255);
            }

            device.UpdateTexture(Texture, pixelData, 0, 0, 0, width, height, 1, 0, 0);
        }

        public void Dispose()
        {
            TextureView.Dispose();
            Texture.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct VertexPositionTexture
    {
        public Vector2 Position;
        public Vector2 TexCoord;
        public const uint SizeInBytes = 16;

        public VertexPositionTexture(Vector2 position, Vector2 texCoord)
        {
            Position = position;
            TexCoord = texCoord;
        }
    }

    public class Game : IDisposable
    {
        private Sdl2Window _window = null!;
        private GraphicsDevice _graphicsDevice = null!;
        private CommandList _commandList = null!;
        private Pipeline _pipeline = null!;
        private DeviceBuffer _vertexBuffer = null!;
        private DeviceBuffer _indexBuffer = null!;
        private DeviceBuffer _projectionBuffer = null!;
        private ResourceLayout _resourceLayout = null!;
        private Sampler _sampler = null!;
        private DeviceTexture _greenPixel = null!;
        private ResourceSet _whitePixelResourceSet = null!;

        // controls texture for top-left message
        private DeviceTexture? _controlsTexture = null;

        const float EarthOrbitRadius = 500f;
        float EarthRadius = 6f;
        float SunRadius = 66.833f;

        SolarBody[] Bodies = Array.Empty<SolarBody>();
        List<Action> actions = new List<Action>();
        int ActionIndex { get; set; }

        RTrigger rtSpace = new RTrigger();
        RTrigger rtRight = new RTrigger();
        RTrigger rtLeft = new RTrigger();
        int DrawCount = 0;
        bool showForces = false;
        RTrigger rtF = new RTrigger();

        public Game()
        {
        }

        public void Run()
        {
            WindowCreateInfo windowCI = new WindowCreateInfo()
            {
                X = 100,
                Y = 100,
                WindowWidth = 1920,
                WindowHeight = 1080,
                WindowTitle = "Solar System - Veldrid/Vulkan"
            };

            GraphicsDeviceOptions options = new GraphicsDeviceOptions
            {
                PreferStandardClipSpaceYDirection = true,
                PreferDepthRangeZeroToOne = true
            };

            VeldridStartup.CreateWindowAndGraphicsDevice(
                windowCI,
                options,
                GraphicsBackend.Vulkan,
                out _window,
                out _graphicsDevice);

            Initialize();
            LoadContent();

            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            double previousTime = 0;

            while (_window.Exists)
            {
                double currentTime = sw.Elapsed.TotalSeconds;
                float deltaTime = (float)(currentTime - previousTime);
                previousTime = currentTime;

                InputSnapshot snapshot = _window.PumpEvents();
                Update(deltaTime, snapshot);
                Draw(deltaTime);
            }

            Dispose();
        }

        private void Initialize()
        {
            _commandList = _graphicsDevice.ResourceFactory.CreateCommandList();

            CreateResources();

            actions.Add(SimpleOrbit);
            actions.Add(BodyOrbitSpiral);
            actions.Add(PartyOverSpiral);
            actions.Add(SunDance);
            actions.Add(SquareGrid);
            actions.Add(BodyCircle);

            ActionIndex = 1;
            actions[ActionIndex]();
        }

        private void CreateResources()
        {
            // Create vertex and index buffers for a quad
            VertexPositionTexture[] vertices = new VertexPositionTexture[]
            {
                new VertexPositionTexture(new Vector2(0, 0), new Vector2(0, 0)),
                new VertexPositionTexture(new Vector2(1, 0), new Vector2(1, 0)),
                new VertexPositionTexture(new Vector2(1, 1), new Vector2(1, 1)),
                new VertexPositionTexture(new Vector2(0, 1), new Vector2(0, 1))
            };

            ushort[] indices = new ushort[] { 0, 1, 2, 0, 2, 3 };

            _vertexBuffer = _graphicsDevice.ResourceFactory.CreateBuffer(
                new BufferDescription(4 * VertexPositionTexture.SizeInBytes, BufferUsage.VertexBuffer));
            _graphicsDevice.UpdateBuffer(_vertexBuffer, 0, vertices);

            _indexBuffer = _graphicsDevice.ResourceFactory.CreateBuffer(
                new BufferDescription(6 * sizeof(ushort), BufferUsage.IndexBuffer));
            _graphicsDevice.UpdateBuffer(_indexBuffer, 0, indices);

            // Create projection buffer
            _projectionBuffer = _graphicsDevice.ResourceFactory.CreateBuffer(
                new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            // Create sampler
            _sampler = _graphicsDevice.ResourceFactory.CreateSampler(new SamplerDescription
            {
                AddressModeU = SamplerAddressMode.Clamp,
                AddressModeV = SamplerAddressMode.Clamp,
                AddressModeW = SamplerAddressMode.Clamp,
                Filter = SamplerFilter.MinLinear_MagLinear_MipLinear,
                LodBias = 0,
                MinimumLod = 0,
                MaximumLod = uint.MaxValue,
                MaximumAnisotropy = 0,
            });

            // Create shaders
            string vertexCode = @"
#version 450

layout(location = 0) in vec2 Position;
layout(location = 1) in vec2 TexCoord;

layout(location = 0) out vec2 fsin_TexCoord;

layout(set = 0, binding = 0) uniform ProjectionBuffer
{
    mat4 Projection;
};

void main()
{
    gl_Position = Projection * vec4(Position, 0, 1);
    fsin_TexCoord = TexCoord;
}";

            string fragmentCode = @"
#version 450

layout(location = 0) in vec2 fsin_TexCoord;
layout(location = 0) out vec4 fsout_Color;

layout(set = 0, binding = 1) uniform texture2D SurfaceTexture;
layout(set = 0, binding = 2) uniform sampler SurfaceSampler;

void main()
{
    fsout_Color = texture(sampler2D(SurfaceTexture, SurfaceSampler), fsin_TexCoord);
}";

            ShaderDescription vertexShaderDesc = new ShaderDescription(
                ShaderStages.Vertex,
                System.Text.Encoding.UTF8.GetBytes(vertexCode),
                "main");
            ShaderDescription fragmentShaderDesc = new ShaderDescription(
                ShaderStages.Fragment,
                System.Text.Encoding.UTF8.GetBytes(fragmentCode),
                "main");

            Shader[] shaders = _graphicsDevice.ResourceFactory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc);

            // Create resource layout
            _resourceLayout = _graphicsDevice.ResourceFactory.CreateResourceLayout(
                new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("ProjectionBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                    new ResourceLayoutElementDescription("SurfaceTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                    new ResourceLayoutElementDescription("SurfaceSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

            // Create pipeline
            GraphicsPipelineDescription pipelineDescription = new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleAlphaBlend,
                DepthStencilState = DepthStencilStateDescription.Disabled,
                RasterizerState = new RasterizerStateDescription(
                    cullMode: FaceCullMode.Back,
                    fillMode: PolygonFillMode.Solid,
                    frontFace: FrontFace.Clockwise,
                    depthClipEnabled: true,
                    scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { _resourceLayout },
                ShaderSet = new ShaderSetDescription(
                    vertexLayouts: new VertexLayoutDescription[]
                    {
                        new VertexLayoutDescription(
                            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                            new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2))
                    },
                    shaders: shaders),
                Outputs = _graphicsDevice.SwapchainFramebuffer.OutputDescription
            };

            _pipeline = _graphicsDevice.ResourceFactory.CreateGraphicsPipeline(pipelineDescription);

            foreach (Shader shader in shaders)
            {
                shader.Dispose();
            }

            // Create reusable white pixel texture for force visualization
            _greenPixel = new DeviceTexture(_graphicsDevice, 1, 1, new RgbaFloat[] { new RgbaFloat(0f, 1f, 0f, 0.5f) });
            _whitePixelResourceSet = _graphicsDevice.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
                _resourceLayout,
                _projectionBuffer,
                _greenPixel.TextureView,
                _sampler));

            // Create controls texture (top-left message)
            string controlsMessage = "Space: restart    Left/Right: next/prev\nF: toggle forces    Esc: exit";
            using Font font = new Font("Segoe UI", 18f, FontStyle.Regular, GraphicsUnit.Pixel);
            _controlsTexture = CreateTextTextureFromString(controlsMessage, font, Color.FromArgb(230, 255, 255, 255));
        }

        // Create a DeviceTexture from rendered text using System.Drawing
        private DeviceTexture CreateTextTextureFromString(string text, Font font, Color textColor)
        {
            // Measure size
            using (Bitmap measureBmp = new Bitmap(1, 1, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(measureBmp))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                SizeF measured = g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
                int width = Math.Max(1, (int)Math.Ceiling(measured.Width));
                int height = Math.Max(1, (int)Math.Ceiling(measured.Height));

                using (Bitmap bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                using (Graphics g2 = Graphics.FromImage(bmp))
                {
                    g2.Clear(Color.Transparent);
                    g2.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    using (SolidBrush brush = new SolidBrush(textColor))
                    {
                        g2.DrawString(text, font, brush, new PointF(0, 0), StringFormat.GenericTypographic);
                    }

                    // Replace this line:
                    // System.Drawing.Imaging.BitmapData bd = bmp.LockBits(new System.Drawing.Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

                    // With this line:
                    System.Drawing.Imaging.BitmapData bd = bmp.LockBits(new System.Drawing.Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    try
                    {
                        int stride = Math.Abs(bd.Stride);
                        byte[] raw = new byte[stride * height];
                        Marshal.Copy(bd.Scan0, raw, 0, raw.Length);

                        RgbaFloat[] data = new RgbaFloat[width * height];
                        for (int y = 0; y < height; y++)
                        {
                            int rowStart = y * stride;
                            for (int x = 0; x < width; x++)
                            {
                                int i = rowStart + x * 4;
                                byte b = raw[i + 0];
                                byte gCol = raw[i + 1];
                                byte r = raw[i + 2];
                                byte a = raw[i + 3];
                                data[y * width + x] = new RgbaFloat(r / 255f, gCol / 255f, b / 255f, a / 255f);
                            }
                        }

                        return new DeviceTexture(_graphicsDevice, (uint)width, (uint)height, data);
                    }
                    finally
                    {
                        bmp.UnlockBits(bd);
                    }
                }
            }
        }

        public DeviceTexture CreateCircle(int radius, int draws = 1)
        {
            draws = Math.Max(1, draws);
            int outerRadius = radius * 2 + 2;
            uint size = (uint)outerRadius;

            RgbaFloat[] data = new RgbaFloat[outerRadius * outerRadius];

            // Set transparent background
            for (int i = 0; i < data.Length; i++)
                data[i] = new RgbaFloat(0, 0, 0, 0);

            // Draw circles
            for (int d = 0; d < draws; d++)
            {
                if (radius <= 0) break;

                int centerX = outerRadius / 2;
                int centerY = outerRadius / 2;
                double angleStep = 1f / (3f * radius);

                for (double angle = 0; angle < 2 * Math.PI; angle += angleStep)
                {
                    double xCircle = radius * Math.Cos(angle);
                    double yCircle = radius * Math.Sin(angle);
                    int x = (int)(centerX + xCircle);
                    int y = (int)(centerY + yCircle);

                    if (x >= 0 && x < outerRadius && y >= 0 && y < outerRadius)
                    {
                        data[y * outerRadius + x] = new RgbaFloat(1, 1, 1, 1);
                    }
                }
                radius--;
            }

            return new DeviceTexture(_graphicsDevice, size, size, data);
        }

        void SimpleOrbit()
        {
            float initialSpeed = 120f;
            float startOrbitScale = 1f;
            float finalOrbitScale = 1f;
            float angleRange = -2f * MathF.PI;
            int Count = 3;
            Bodies = new SolarBody[Count];

            Bodies[Body.Sun].Position = new Vector2(_window.Width / 2, _window.Height / 2);
            Bodies[Body.Sun].Velocity = new Vector2(0f, 0f);
            Bodies[Body.Sun].Mass = 333054.2531815f;
            Bodies[Body.Sun].Radius = SunRadius;
            Bodies[Body.Sun].Color = new RgbaFloat(1, 1, 0, 1); // Yellow

            RgbaFloat[] bodyColor = new RgbaFloat[] {
                new RgbaFloat(1, 0, 0, 1), // Red
                new RgbaFloat(0, 0, 1, 1), // Blue
                new RgbaFloat(0, 1, 0, 1), // Green
                new RgbaFloat(0.93f, 0.51f, 0.93f, 1), // Violet
                new RgbaFloat(0.5f, 0, 0.5f, 1), // Purple
                new RgbaFloat(1, 0.65f, 0, 1), // Orange
                new RgbaFloat(1, 0.27f, 0, 1) // OrangeRed
            };

            float orbitScale = finalOrbitScale - startOrbitScale;
            for (int i = 1; i < Bodies.Length; i++)
            {
                float unitStep = (float)(i - 1) / (float)(Bodies.Length - 1);
                float angle = unitStep * angleRange;
                float scale = startOrbitScale + unitStep * orbitScale;
                int color = i % bodyColor.Length;
                Vector2 bodyUnitX = EarthOrbitRadius * Vector2.UnitX;
                Vector2 toBody = bodyUnitX.Rotate(angle);
                Bodies[i].Position = Bodies[Body.Sun].Position + scale * toBody;
                Bodies[i].Velocity = 1f / scale * initialSpeed * toBody.ClockwisePerpendicular().Unit();
                Bodies[i].Mass = 1f;
                Bodies[i].Radius = EarthRadius;
                Bodies[i].Color = bodyColor[color];
            }

            CreateTextures();
        }

        void CreateTextures()
        {
            for (int i = 0; i < Bodies.Length; i++)
            {
                Bodies[i].Texture?.Dispose();
                Bodies[i].Texture = CreateCircle((int)Bodies[i].Radius, (int)(0.200f * Bodies[i].Radius));
            }
        }

        void BodyOrbitSpiral()
        {
            float initialSpeed = 105;
            float startOrbitScale = 0.750f;
            float finalOrbitScale = 1.25f;
            float angleRange = -6f * MathF.PI;
            int Count = 129;
            Bodies = new SolarBody[Count];

            Bodies[Body.Sun].Position = new Vector2(_window.Width / 2, _window.Height / 2);
            Bodies[Body.Sun].Velocity = new Vector2(-0.001f, 0f);
            Bodies[Body.Sun].Mass = 333054.2531815f;
            Bodies[Body.Sun].Radius = SunRadius;
            Bodies[Body.Sun].Color = new RgbaFloat(1, 1, 0, 1);

            RgbaFloat[] bodyColor = new RgbaFloat[] {
                new RgbaFloat(1, 0, 0, 1),
                new RgbaFloat(0, 0, 1, 1),
                new RgbaFloat(0, 1, 0, 1),
                new RgbaFloat(0.93f, 0.51f, 0.93f, 1),
                new RgbaFloat(0.5f, 0, 0.5f, 1),
                new RgbaFloat(1, 0.65f, 0, 1),
                new RgbaFloat(1, 0.27f, 0, 1)
            };

            float orbitScale = finalOrbitScale - startOrbitScale;
            for (int i = 1; i < Bodies.Length; i++)
            {
                float unitStep = (float)(i - 1) / (float)(Bodies.Length - 1);
                float angle = unitStep * angleRange;
                float scale = startOrbitScale + unitStep * orbitScale;
                int color = i % bodyColor.Length;
                Vector2 bodyUnitX = EarthOrbitRadius * Vector2.UnitX;
                Vector2 toBody = bodyUnitX.Rotate(angle);
                Bodies[i].Position = Bodies[Body.Sun].Position + scale * toBody;
                Bodies[i].Velocity = 1f / scale * initialSpeed * toBody.ClockwisePerpendicular().Unit();
                Bodies[i].Mass = 1f;
                Bodies[i].Radius = EarthRadius;
                Bodies[i].Color = bodyColor[color];
            }
            CreateTextures();
        }

        void PartyOverSpiral()
        {
            float initialSpeed = 30f;
            float startOrbitScale = 0.125f;
            float finalOrbitScale = 1.5f;
            float angleRange = -12f * MathF.PI;
            int Count = 129;
            Bodies = new SolarBody[Count];

            Bodies[Body.Sun].Position = new Vector2(_window.Width / 2, _window.Height / 2);
            Bodies[Body.Sun].Velocity = new Vector2(0f, 0f);
            Bodies[Body.Sun].Mass = 333054.2531815f;
            Bodies[Body.Sun].Radius = SunRadius;
            Bodies[Body.Sun].Color = new RgbaFloat(1, 1, 0, 1);

            RgbaFloat[] bodyColor = new RgbaFloat[] {
                new RgbaFloat(1, 0, 0, 1),
                new RgbaFloat(0, 0, 1, 1),
                new RgbaFloat(0, 1, 0, 1),
                new RgbaFloat(0.93f, 0.51f, 0.93f, 1),
                new RgbaFloat(0.5f, 0, 0.5f, 1),
                new RgbaFloat(1, 0.65f, 0, 1),
                new RgbaFloat(1, 0.27f, 0, 1)
            };

            float orbitScale = finalOrbitScale - startOrbitScale;
            for (int i = 1; i < Bodies.Length; i++)
            {
                float unitStep = (float)(i - 1) / (float)(Bodies.Length - 1);
                float angle = unitStep * angleRange;
                float scale = startOrbitScale + unitStep * orbitScale;
                int color = i % bodyColor.Length;
                Vector2 bodyUnitX = EarthOrbitRadius * Vector2.UnitX;
                Vector2 toBody = bodyUnitX.Rotate(angle);
                Bodies[i].Position = Bodies[Body.Sun].Position + scale * toBody;
                Bodies[i].Velocity = 1f / scale * initialSpeed * toBody.ClockwisePerpendicular().Unit();
                Bodies[i].Mass = 0.25f;
                Bodies[i].Radius = EarthRadius;
                Bodies[i].Color = bodyColor[color];
            }
            CreateTextures();
        }

        void BodyCircle()
        {
            float initialSpeed = 125f;
            float startOrbitScale = 1f;
            float finalOrbitScale = 1f;
            float angleRange = -6f * MathF.PI;
            int Count = 129;
            Bodies = new SolarBody[Count];

            Bodies[Body.Sun].Position = new Vector2(_window.Width / 2, _window.Height / 2);
            Bodies[Body.Sun].Velocity = new Vector2(0f, 0f);
            Bodies[Body.Sun].Mass = 333054.2531815f;
            Bodies[Body.Sun].Radius = SunRadius;
            Bodies[Body.Sun].Color = new RgbaFloat(1, 1, 0, 1);

            RgbaFloat[] bodyColor = new RgbaFloat[] {
                new RgbaFloat(1, 0, 0, 1),
                new RgbaFloat(0, 0, 1, 1),
                new RgbaFloat(0, 1, 0, 1),
                new RgbaFloat(0.93f, 0.51f, 0.93f, 1),
                new RgbaFloat(0.5f, 0, 0.5f, 1),
                new RgbaFloat(1, 0.65f, 0, 1),
                new RgbaFloat(1, 0.27f, 0, 1)
            };

            float orbitScale = finalOrbitScale - startOrbitScale;
            for (int i = 1; i < Bodies.Length; i++)
            {
                float unitStep = (float)(i - 1) / (float)(Bodies.Length - 1);
                float angle = unitStep * angleRange;
                float scale = startOrbitScale + unitStep * orbitScale;
                int color = i % bodyColor.Length;
                Vector2 bodyUnitX = EarthOrbitRadius * Vector2.UnitX;
                Vector2 toBody = bodyUnitX.Rotate(angle);
                Bodies[i].Position = Bodies[Body.Sun].Position + scale * toBody;
                Bodies[i].Velocity = 1f / scale * initialSpeed * toBody.ClockwisePerpendicular().Unit();
                Bodies[i].Mass = 10f;
                Bodies[i].Radius = EarthRadius;
                Bodies[i].Color = bodyColor[color];
            }

            CreateTextures();
        }

        void SquareGrid(int rows, int columns)
        {
            int Count = rows * columns;
            Bodies = new SolarBody[Count];

            int body = 0;
            RgbaFloat[] bodyColor = new RgbaFloat[] {
                new RgbaFloat(1, 0, 0, 1),
                new RgbaFloat(0, 0, 1, 1),
                new RgbaFloat(0, 1, 0, 1),
                new RgbaFloat(0.93f, 0.51f, 0.93f, 1),
                new RgbaFloat(0.5f, 0, 0.5f, 1),
                new RgbaFloat(1, 0.65f, 0, 1),
                new RgbaFloat(1, 0.27f, 0, 1)
            };

            for (int row = 0; row < rows; row++)
            {
                float unitStepX = (float)(row) / (float)(rows - 1);
                for (int col = 0; col < columns; col++)
                {
                    float unitStepY = (float)(col) / (float)(columns - 1);
                    Vector2 gridPosition = new Vector2(unitStepX * _window.Width, unitStepY * _window.Height);
                    Bodies[body].Position = gridPosition;
                    Bodies[body].Velocity = Vector2.Zero;
                    Bodies[body].Mass = 1000f;
                    Bodies[body].Radius = EarthRadius;
                    Bodies[body].Color = bodyColor[body % bodyColor.Length];
                    body++;
                }
            }
            CreateTextures();
        }

        void SquareGrid()
        {
            SquareGrid(15, 10);
        }

        void SunDance()
        {
            float initialSpeed = 350f;
            int Count = 3;
            Bodies = new SolarBody[Count];

            Bodies[Body.Sun].Position = new Vector2(_window.Width / 2, _window.Height / 2);
            Bodies[Body.Sun].Velocity = new Vector2(0f, 0f);
            Bodies[Body.Sun].Mass = 1;
            Bodies[Body.Sun].Radius = EarthRadius;
            Bodies[Body.Sun].Color = new RgbaFloat(1, 1, 0, 1);

            Vector2 bodyUnitX = EarthOrbitRadius * Vector2.UnitX;
            Vector2 toBody = bodyUnitX.Rotate(0);
            Bodies[1].Position = Bodies[Body.Sun].Position + toBody;
            Bodies[1].Velocity = initialSpeed * toBody.ClockwisePerpendicular().Unit();
            Bodies[1].Mass = 700000f;
            Bodies[1].Radius = EarthRadius;
            Bodies[1].Color = new RgbaFloat(0, 0, 1, 1);

            Bodies[2].Position = Bodies[Body.Sun].Position + toBody + 100f * Vector2.UnitX;
            Bodies[2].Velocity = -initialSpeed * toBody.ClockwisePerpendicular().Unit();
            Bodies[2].Mass = 700000f;
            Bodies[2].Radius = EarthRadius;
            Bodies[2].Color = new RgbaFloat(0, 1, 0, 1);

            CreateTextures();
        }

        private void LoadContent()
        {
            // Content is created procedurally now
        }

        private void DrawForceVectors(Vector2[] positions)
        {
            const float forceScale = 0.5f; // Scale factor for visualization
            const float lineWidth = 2.0f;

            // Draw net force for each body (much more efficient than individual forces)
            for (int from = 0; from < Bodies.Length; from++)
            {
                // Calculate net force on this body
                Vector2 netForce = Bodies.AllBodies(from);
                float forceMagnitude = netForce.Length();
                
                if (forceMagnitude < 0.01f) continue; // Skip negligible forces

                Vector2 forceNormalized = Vector2.Normalize(netForce);
                Vector2 forceVector = forceNormalized * forceMagnitude * forceScale;

                Vector2 start = positions[from];
                Vector2 end = start + forceVector;

                DrawLine(start, end, new RgbaFloat(1f, 0f, 0f, 0.5f), lineWidth);
                DrawArrowhead(end, forceNormalized, new RgbaFloat(1f, 0f, 0f, 0.7f), 8f);
            }
        }

        private void DrawLine(Vector2 start, Vector2 end, RgbaFloat color, float width)
        {
            // Calculate line direction and perpendicular
            Vector2 direction = end - start;
            float length = direction.Length();
            if (length < 0.1f) return;

            Vector2 perpendicular = new Vector2(-direction.Y, direction.X);
            perpendicular = Vector2.Normalize(perpendicular) * (width / 2);

            // Create quad vertices for the line (colored via vertex colors would be better, but using texture coordinates as color)
            Vector2 p1 = start + perpendicular;
            Vector2 p2 = start - perpendicular;
            Vector2 p3 = end - perpendicular;
            Vector2 p4 = end + perpendicular;

            _commandList.SetGraphicsResourceSet(0, _whitePixelResourceSet);

            VertexPositionTexture[] vertices = new VertexPositionTexture[]
            {
                new VertexPositionTexture(p1, new Vector2(color.R, color.G)),
                new VertexPositionTexture(p2, new Vector2(color.B, color.A)),
                new VertexPositionTexture(p3, new Vector2(color.R, color.G)),
                new VertexPositionTexture(p4, new Vector2(color.B, color.A))
            };

            _commandList.UpdateBuffer(_vertexBuffer, 0, vertices);
            _commandList.DrawIndexed(6);
        }

        private void DrawArrowhead(Vector2 tip, Vector2 direction, RgbaFloat color, float size)
        {
            // Create arrowhead pointing in the direction of the force
            Vector2 perpendicular = new Vector2(-direction.Y, direction.X);
            
            Vector2 p1 = tip;
            Vector2 p2 = tip - direction * size + perpendicular * (size * 0.5f);
            Vector2 p3 = tip - direction * size - perpendicular * (size * 0.5f);

            // Draw as a filled triangle (use two triangles to form it)
            Vector2 center = (p1 + p2 + p3) / 3;

            _commandList.SetGraphicsResourceSet(0, _whitePixelResourceSet);

            VertexPositionTexture[] vertices = new VertexPositionTexture[]
            {
                new VertexPositionTexture(p1, new Vector2(color.R, color.G)),
                new VertexPositionTexture(p2, new Vector2(color.B, color.A)),
                new VertexPositionTexture(p3, new Vector2(color.R, color.G)),
                new VertexPositionTexture(center, new Vector2(color.B, color.A))
            };

            _commandList.UpdateBuffer(_vertexBuffer, 0, vertices);
            _commandList.DrawIndexed(6);
        }

        private void Update(float deltaTime, InputSnapshot snapshot)
        {
            bool spacePressed = false;
            bool rightPressed = false;
            bool leftPressed = false;

            bool fPressed = false;

            foreach (KeyEvent ke in snapshot.KeyEvents)
            {
                if (ke.Down)
                {
                    if (ke.Key == Key.Space) spacePressed = true;
                    if (ke.Key == Key.Right) rightPressed = true;
                    if (ke.Key == Key.Left) leftPressed = true;
                    if (ke.Key == Key.F) fPressed = true;
                    if (ke.Key == Key.Escape) _window.Close();
                }
            }

            if (rtF.CLK(fPressed))
            {
                showForces = !showForces;
            }

            if (rtSpace.CLK(spacePressed))
            {
                actions[ActionIndex]();
            }
            if (rtRight.CLK(rightPressed))
            {
                ActionIndex++;
                if (ActionIndex >= actions.Count)
                    ActionIndex = 0;
                actions[ActionIndex]();
            }
            if (rtLeft.CLK(leftPressed))
            {
                ActionIndex--;
                if (ActionIndex < 0)
                    ActionIndex = actions.Count - 1;
                actions[ActionIndex]();
            }
        }

        private void Draw(float deltaTime)
        {
            float T = deltaTime;
            if (T > 0.020f)
                T = 0.020f;

            DrawCount++;

            // Physics calculations
            Vector2[] accel = new Vector2[Bodies.Length];
            for (int selected = 0; selected < Bodies.Length; selected++)
            {
                Vector2 sumForce = Bodies.AllBodies(selected);
                accel[selected] = (1.0f / Bodies[selected].Mass) * sumForce;
            }

            Vector2[] nextVelocity = new Vector2[Bodies.Length];
            Vector2[] nextPosition = new Vector2[Bodies.Length];
            for (int i = 0; i < Bodies.Length; i++)
            {
                nextVelocity[i] = T * accel[i] + Bodies[i].Velocity;
                nextPosition[i] = (T * T / 2f) * accel[i] + T * Bodies[i].Velocity + Bodies[i].Position;
            }

            // Rendering
            _commandList.Begin();
            _commandList.SetFramebuffer(_graphicsDevice.SwapchainFramebuffer);
            _commandList.ClearColorTarget(0, RgbaFloat.Black);
            _commandList.SetPipeline(_pipeline);

            // Update projection matrix
            Matrix4x4 projection = Matrix4x4.CreateOrthographicOffCenter(
                0, _window.Width, _window.Height, 0, 0, 1);
            _commandList.UpdateBuffer(_projectionBuffer, 0, ref projection);

            _commandList.SetVertexBuffer(0, _vertexBuffer);
            _commandList.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);

            // Draw force vectors if enabled
            if (showForces)
            {
                DrawForceVectors(nextPosition);
            }

            // Draw each body
            for (int i = 0; i < Bodies.Length; i++)
            {
                if (Bodies[i].Texture == null) continue;

                DeviceTexture texture = Bodies[i].Texture;

                // Create resource set for this body
                ResourceSet resourceSet = _graphicsDevice.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
                    _resourceLayout,
                    _projectionBuffer,
                    texture.TextureView,
                    _sampler));

                _commandList.SetGraphicsResourceSet(0, resourceSet);

                // Update vertex positions for this sprite
                Vector2 position = nextPosition[i];
                float radius = Bodies[i].Radius;
                Vector2 topLeft = position + new Vector2(-radius, -radius);
                Vector2 size = new Vector2(radius * 2, radius * 2);

                VertexPositionTexture[] vertices = new VertexPositionTexture[]
                {
                    new VertexPositionTexture(topLeft, new Vector2(0, 0)),
                    new VertexPositionTexture(topLeft + new Vector2(size.X, 0), new Vector2(1, 0)),
                    new VertexPositionTexture(topLeft + size, new Vector2(1, 1)),
                    new VertexPositionTexture(topLeft + new Vector2(0, size.Y), new Vector2(0, 1))
                };

                _commandList.UpdateBuffer(_vertexBuffer, 0, vertices);
                _commandList.DrawIndexed(6);

                resourceSet.Dispose();

                // Update physics
                Bodies[i].Position = nextPosition[i];
                Bodies[i].Velocity = nextVelocity[i];
            }

            // Draw controls texture in top-left
            if (_controlsTexture != null)
            {
                ResourceSet controlSet = _graphicsDevice.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
                    _resourceLayout,
                    _projectionBuffer,
                    _controlsTexture.TextureView,
                    _sampler));

                _commandList.SetGraphicsResourceSet(0, controlSet);

                Vector2 pos = new Vector2(8, 8); // margin from top-left
                Vector2 size = new Vector2((float)_controlsTexture.Width, (float)_controlsTexture.Height);

                VertexPositionTexture[] ctrlVerts = new VertexPositionTexture[]
                {
                    new VertexPositionTexture(pos, new Vector2(0, 0)),
                    new VertexPositionTexture(pos + new Vector2(size.X, 0), new Vector2(1, 0)),
                    new VertexPositionTexture(pos + size, new Vector2(1, 1)),
                    new VertexPositionTexture(pos + new Vector2(0, size.Y), new Vector2(0, 1))
                };

                _commandList.UpdateBuffer(_vertexBuffer, 0, ctrlVerts);
                _commandList.DrawIndexed(6);

                controlSet.Dispose();
            }

            _commandList.End();
            _graphicsDevice.SubmitCommands(_commandList);
            _graphicsDevice.SwapBuffers();
        }

        public void Dispose()
        {
            foreach (var body in Bodies)
            {
                body.Texture?.Dispose();
            }

            _controlsTexture?.Dispose();
            _whitePixelResourceSet?.Dispose();
            _greenPixel?.Dispose();
            _pipeline.Dispose();
            _commandList.Dispose();
            _vertexBuffer.Dispose();
            _indexBuffer.Dispose();
            _projectionBuffer.Dispose();
            _resourceLayout.Dispose();
            _sampler.Dispose();
            _graphicsDevice.Dispose();
        }
    }
}
