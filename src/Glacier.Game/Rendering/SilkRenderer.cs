namespace Glacier.Game.Rendering;

using System;
using System.Numerics;
using Silk.NET.OpenGL;

/// <summary>
/// Hardware GPU renderer using Silk.NET OpenGL for modern shader-based batched 2D rendering.
/// </summary>
public sealed unsafe class SilkRenderer : IRenderer
{
    private readonly GL? _gl;
    private uint _vao;
    private uint _vbo;
    private uint _ebo;
    private uint _shaderProgram;
    private int _uProjectionLoc;
    private int _uTextureLoc;
    private uint _whiteTexture;
    private bool _initialized;
    private bool _disposed;

    public int Width { get; private set; }
    public int Height { get; private set; }

    public SilkRenderer(GL? gl = null, int width = 1920, int height = 1080)
    {
        _gl = gl;
        Width = width;
        Height = height;

        if (_gl != null)
        {
            Initialize(width, height);
        }
    }

    public void Initialize(int width, int height)
    {
        Width = width;
        Height = height;

        if (_gl == null || _initialized) return;

        // 1. Compile Shader Program for batched 2D quad rendering
        const string vertexShaderSource = @"#version 330 core
layout (location = 0) in vec2 aPos;
layout (location = 1) in vec2 aTexCoord;
layout (location = 2) in vec4 aColor;

out vec2 vTexCoord;
out vec4 vColor;
uniform mat4 uProjection;

void main()
{
    gl_Position = uProjection * vec4(aPos, 0.0, 1.0);
    vTexCoord = aTexCoord;
    vColor = aColor;
}";

        const string fragmentShaderSource = @"#version 330 core
in vec2 vTexCoord;
in vec4 vColor;
out vec4 FragColor;

uniform sampler2D uTexture;

void main()
{
    FragColor = texture(uTexture, vTexCoord) * vColor;
}";

        uint vs = _gl.CreateShader(ShaderType.VertexShader);
        _gl.ShaderSource(vs, vertexShaderSource);
        _gl.CompileShader(vs);

        uint fs = _gl.CreateShader(ShaderType.FragmentShader);
        _gl.ShaderSource(fs, fragmentShaderSource);
        _gl.CompileShader(fs);

        _shaderProgram = _gl.CreateProgram();
        _gl.AttachShader(_shaderProgram, vs);
        _gl.AttachShader(_shaderProgram, fs);
        _gl.LinkProgram(_shaderProgram);

        _gl.DeleteShader(vs);
        _gl.DeleteShader(fs);

        _uProjectionLoc = _gl.GetUniformLocation(_shaderProgram, "uProjection");
        _uTextureLoc = _gl.GetUniformLocation(_shaderProgram, "uTexture");

        // 1x1 white default texture
        _whiteTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _whiteTexture);
        uint whitePixel = 0xFFFFFFFF;
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, 1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, &whitePixel);
        int nearestFilter = (int)TextureMinFilter.Nearest;
        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, in nearestFilter);
        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, in nearestFilter);
        _gl.BindTexture(TextureTarget.Texture2D, 0);

        // 2. Setup dynamic VAO, VBO, EBO
        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _ebo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        // Pre-allocate 4MB dynamic vertex buffer
        _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(4 * 1024 * 1024), null, BufferUsageARB.DynamicDraw);

        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(1024 * 1024), null, BufferUsageARB.DynamicDraw);

        // Vertex layout: Position(2 floats), TexCoord(2 floats), Color(4 bytes normalized)
        uint stride = (uint)sizeof(Vertex2D);

        // Location 0: Position
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);

        // Location 1: TexCoord
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)(sizeof(float) * 2));

        // Location 2: Color (packed 4 bytes normalized)
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, stride, (void*)(sizeof(float) * 4));

        _gl.BindVertexArray(0);

        _initialized = true;
    }

    public void Begin(in Matrix3x2 transform)
    {
        if (_gl == null) return;
        _gl.Viewport(0, 0, (uint)Width, (uint)Height);
        _gl.ClearColor(0.05f, 0.07f, 0.11f, 1.0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        _gl.UseProgram(_shaderProgram);

        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _whiteTexture);
        if (_uTextureLoc >= 0)
        {
            _gl.Uniform1(_uTextureLoc, 0);
        }

        // Orthographic projection matrix: top-left (0,0) to bottom-right (Width, Height)
        float[] ortho = new float[16]
        {
            2f / Width, 0f, 0f, 0f,
            0f, -2f / Height, 0f, 0f,
            0f, 0f, 1f, 0f,
            -1f, 1f, 0f, 1f
        };
        fixed (float* pMat = ortho)
        {
            _gl.UniformMatrix4(_uProjectionLoc, 1, false, pMat);
        }

        _gl.BindVertexArray(_vao);
    }

    public void DrawBatch(ReadOnlySpan<Vertex2D> vertices, ReadOnlySpan<uint> indices)
    {
        DrawBatch(vertices, indices, 0);
    }

    public void DrawBatch(ReadOnlySpan<Vertex2D> vertices, ReadOnlySpan<uint> indices, int textureId)
    {
        if (_gl == null || vertices.Length == 0) return;

        uint texId = textureId > 0 ? (uint)textureId : _whiteTexture;
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, texId);
        if (_uTextureLoc >= 0)
        {
            _gl.Uniform1(_uTextureLoc, 0);
        }

        fixed (Vertex2D* pV = vertices)
        fixed (uint* pI = indices)
        {
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(sizeof(Vertex2D) * vertices.Length), pV);

            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
            _gl.BufferSubData(BufferTargetARB.ElementArrayBuffer, 0, (nuint)(sizeof(uint) * indices.Length), pI);

            _gl.DrawElements(PrimitiveType.Triangles, (uint)indices.Length, DrawElementsType.UnsignedInt, (void*)0);
        }
    }

    public void End()
    {
        if (_gl == null) return;
        _gl.BindVertexArray(0);
        _gl.UseProgram(0);
    }

    public void Present()
    {
        // Buffers are swapped by host window lifecycle
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_gl != null && _initialized)
        {
            if (_whiteTexture != 0)
            {
                _gl.DeleteTexture(_whiteTexture);
                _whiteTexture = 0;
            }
            _gl.DeleteProgram(_shaderProgram);
            _gl.DeleteBuffer(_vbo);
            _gl.DeleteBuffer(_ebo);
            _gl.DeleteVertexArray(_vao);
        }
    }
}
