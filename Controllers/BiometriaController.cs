using System.Data;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace SigelabBackend.Controllers;

public class BiometriaForm
{
    public int IdUsuario { get; set; }
    public IFormFile? Foto { get; set; }
}

[ApiController]
[Route("api/[controller]")]
public class BiometriaController : ControllerBase
{
    private const long MaxBytes = 8 * 1024 * 1024;

    // Servicio Python local que convierte la foto en un vector.
    private static readonly HttpClient Servicio = new()
    {
        BaseAddress = new Uri("http://127.0.0.1:8001"),
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static readonly JsonSerializerOptions JsonOpciones = new(JsonSerializerDefaults.Web);

    private sealed record EmbeddingResp(float[] Embedding, string Modelo, int Dimension, double Calidad);

    private readonly string _connectionString;
    private readonly byte[] _clave;
    private readonly double _umbral;
    private readonly ILogger<BiometriaController> _logger;

    public BiometriaController(IConfiguration configuration, ILogger<BiometriaController> logger)
    {
        _connectionString = configuration.GetConnectionString("SqlServerConn")
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Falta configurar SqlServerConn o DefaultConnection.");

        var claveB64 = configuration["Biometria:ClaveBase64"]
            ?? throw new InvalidOperationException("Falta Biometria:ClaveBase64.");
        _clave = Convert.FromBase64String(claveB64);
        if (_clave.Length != 32)
            throw new InvalidOperationException("Biometria:ClaveBase64 debe ser de 32 bytes.");

        // Umbral de similitud coseno recomendado para SFace: 0.363.
        _umbral = double.TryParse(configuration["Biometria:Umbral"], NumberStyles.Float,
            CultureInfo.InvariantCulture, out var u) ? u : 0.363;
        _logger = logger;
    }

    // POST api/Biometria/registrar  (multipart: idUsuario, foto)
    [HttpPost("registrar")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> Registrar([FromForm] BiometriaForm form, CancellationToken ct)
    {
        if (form.IdUsuario <= 0)
            return BadRequest(new { exito = false, mensaje = "ID de usuario invalido." });

        var (datos, error) = await ObtenerEmbeddingAsync(form.Foto, ct);
        if (error is not null) return error;

        var cifrado = Cifrar(datos!.Embedding, form.IdUsuario);
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand("dbo.sp_biometria_registrar", conn)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 30
            };
            cmd.Parameters.Add("@id_usuario", SqlDbType.Int).Value = form.IdUsuario;
            cmd.Parameters.Add("@embedding_cifrado", SqlDbType.VarBinary, -1).Value = cifrado;
            cmd.Parameters.Add("@modelo", SqlDbType.NVarChar, 100).Value = datos.Modelo;
            cmd.Parameters.Add("@dimension_embedding", SqlDbType.Int).Value = datos.Dimension;
            var calidad = cmd.Parameters.Add("@calidad_promedio", SqlDbType.Decimal);
            calidad.Precision = 9;
            calidad.Scale = 4;
            calidad.Value = (decimal)Math.Round(datos.Calidad, 4);
            await cmd.ExecuteNonQueryAsync(ct);

            return Ok(new { exito = true, mensaje = "Biometria registrada correctamente." });
        }
        catch (SqlException ex) when (ex.Number >= 50000)
        {
            var respuesta = new { exito = false, mensaje = ex.Message };
            return ex.Number == 50002 ? Conflict(respuesta) : BadRequest(respuesta);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Error SQL al registrar biometria del usuario {Id}", form.IdUsuario);
            return StatusCode(500, new { exito = false, mensaje = "Error al guardar la biometria." });
        }
    }

    // POST api/Biometria/validar  (multipart: idUsuario, foto)
    [HttpPost("validar")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> Validar([FromForm] BiometriaForm form, CancellationToken ct)
    {
        if (form.IdUsuario <= 0)
            return BadRequest(new { exito = false, mensaje = "ID de usuario invalido." });

        byte[]? guardado = null;
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand("dbo.sp_biometria_obtener", conn)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 30
            };
            cmd.Parameters.Add("@id_usuario", SqlDbType.Int).Value = form.IdUsuario;
            await using var lector = await cmd.ExecuteReaderAsync(ct);
            if (await lector.ReadAsync(ct))
                guardado = (byte[])lector["embedding_cifrado"];
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Error SQL al leer biometria del usuario {Id}", form.IdUsuario);
            return StatusCode(500, new { exito = false, mensaje = "Error al consultar la biometria." });
        }

        if (guardado is null)
            return NotFound(new
            {
                exito = false,
                es_valido = false,
                requiere_registro = true,
                mensaje = "El usuario no tiene un rostro registrado."
            });

        var (datos, error) = await ObtenerEmbeddingAsync(form.Foto, ct);
        if (error is not null) return error;

        float[] referencia;
        try
        {
            referencia = Descifrar(guardado, form.IdUsuario);
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "No se pudo descifrar la biometria del usuario {Id}", form.IdUsuario);
            return StatusCode(500, new { exito = false, mensaje = "Error al leer la biometria guardada." });
        }

        if (referencia.Length != datos!.Embedding.Length)
            return Conflict(new { exito = false, mensaje = "La biometria guardada usa otro modelo. Debe registrarse de nuevo." });

        var similitud = Coseno(referencia, datos.Embedding);
        var valido = similitud >= _umbral;
        // La similitud solo se registra en el log: no se devuelve al cliente.
        _logger.LogInformation("Validacion biometrica usuario {Id}: similitud {S:F3} umbral {U:F3} valido={V}",
            form.IdUsuario, similitud, _umbral, valido);

        return Ok(new
        {
            exito = true,
            es_valido = valido,
            mensaje = valido ? "Rostro verificado." : "El rostro no coincide."
        });
    }

    private async Task<(EmbeddingResp? Datos, IActionResult? Error)> ObtenerEmbeddingAsync(
        IFormFile? foto, CancellationToken ct)
    {
        if (foto is null || foto.Length == 0)
            return (null, BadRequest(new { exito = false, mensaje = "Debe enviar la foto." }));
        if (foto.Length > MaxBytes)
            return (null, StatusCode(413, new { exito = false, mensaje = "La imagen es demasiado grande." }));

        using var contenido = new MultipartFormDataContent();
        await using var flujo = foto.OpenReadStream();
        var parte = new StreamContent(flujo);
        parte.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(foto.ContentType) ? "image/jpeg" : foto.ContentType);
        contenido.Add(parte, "foto", string.IsNullOrWhiteSpace(foto.FileName) ? "foto.jpg" : foto.FileName);

        try
        {
            using var respuesta = await Servicio.PostAsync("/embedding", contenido, ct);
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);

            if (respuesta.IsSuccessStatusCode)
            {
                var datos = JsonSerializer.Deserialize<EmbeddingResp>(cuerpo, JsonOpciones);
                if (datos?.Embedding is { Length: > 0 })
                    return (datos, null);
                return (null, StatusCode(502, new { exito = false, mensaje = "Respuesta invalida del servicio facial." }));
            }

            var codigo = (int)respuesta.StatusCode;
            if (codigo is 400 or 413 or 422)
                return (null, UnprocessableEntity(new { exito = false, mensaje = MensajeDe(cuerpo) }));

            _logger.LogError("El servicio facial respondio {Codigo}: {Cuerpo}", codigo, cuerpo);
            return (null, StatusCode(502, new { exito = false, mensaje = "Error en el servicio de reconocimiento facial." }));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "No se pudo contactar al servicio facial");
            return (null, StatusCode(503, new { exito = false, mensaje = "El servicio de reconocimiento facial no esta disponible." }));
        }
    }

    private static string MensajeDe(string cuerpo)
    {
        try
        {
            using var doc = JsonDocument.Parse(cuerpo);
            if (doc.RootElement.TryGetProperty("detail", out var detalle) && detalle.ValueKind == JsonValueKind.String)
                return detalle.GetString()!;
        }
        catch (JsonException) { }
        return "No se pudo procesar la imagen.";
    }

    // AES-GCM. Formato guardado: nonce(12) + etiqueta(16) + datos. El id de usuario va como
    // dato asociado, asi un vector no se puede copiar a otro usuario.
    private byte[] Cifrar(float[] vector, int idUsuario)
    {
        var plano = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, plano, 0, plano.Length);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var etiqueta = new byte[16];
        var cifrado = new byte[plano.Length];
        using var aes = new AesGcm(_clave, 16);
        aes.Encrypt(nonce, plano, cifrado, etiqueta, BitConverter.GetBytes(idUsuario));

        var salida = new byte[12 + 16 + cifrado.Length];
        Buffer.BlockCopy(nonce, 0, salida, 0, 12);
        Buffer.BlockCopy(etiqueta, 0, salida, 12, 16);
        Buffer.BlockCopy(cifrado, 0, salida, 28, cifrado.Length);
        return salida;
    }

    private float[] Descifrar(byte[] datos, int idUsuario)
    {
        if (datos.Length <= 28) throw new CryptographicException("Dato biometrico invalido.");
        var nonce = datos[..12];
        var etiqueta = datos[12..28];
        var cifrado = datos[28..];
        var plano = new byte[cifrado.Length];
        using var aes = new AesGcm(_clave, 16);
        aes.Decrypt(nonce, cifrado, etiqueta, plano, BitConverter.GetBytes(idUsuario));

        var vector = new float[plano.Length / sizeof(float)];
        Buffer.BlockCopy(plano, 0, vector, 0, plano.Length);
        return vector;
    }

    private static double Coseno(float[] a, float[] b)
    {
        double punto = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            punto += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        return na == 0 || nb == 0 ? 0 : punto / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}
