using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;

namespace SigelabBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuditoriaController : ControllerBase
{
    private readonly string _connectionString;
    private readonly ILogger<AuditoriaController> _logger;

    public AuditoriaController(IConfiguration configuration, ILogger<AuditoriaController> logger)
    {
        _connectionString = configuration.GetConnectionString("SqlServerConn")
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("No se configuró la conexión a SQL Server.");
        _logger = logger;
    }

    private async Task<List<Dictionary<string, object?>>> ConsultarAsync(
        string procedimiento, object parametros, CancellationToken ct)
    {
        var registros = new List<Dictionary<string, object?>>();
        await using var conexion = new SqlConnection(_connectionString);
        await using var comando = new SqlCommand(procedimiento, conexion)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 60
        };
        comando.Parameters.Add("@json", SqlDbType.NVarChar, -1).Value =
            JsonSerializer.Serialize(parametros);
        await conexion.OpenAsync(ct);
        await using var lector = await comando.ExecuteReaderAsync(ct);
        while (await lector.ReadAsync(ct))
        {
            var fila = new Dictionary<string, object?>();
            for (var i = 0; i < lector.FieldCount; i++)
                fila[lector.GetName(i)] = await lector.IsDBNullAsync(i, ct)
                    ? null : lector.GetValue(i);
            registros.Add(fila);
        }
        return registros;
    }

    // GET /api/Auditoria?idUsuarioAccionante=5
    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] int idUsuarioAccionante, CancellationToken ct)
    {
        if (idUsuarioAccionante <= 0)
            return BadRequest(new { mensaje = "idUsuarioAccionante debe ser mayor que cero." });

        try
        {
            var registros = await ConsultarAsync(
                "dbo.sp_Auditoria_Listar",
                new { usuarioAccionante = new { idUsuario = idUsuarioAccionante } }, ct);
            return Ok(registros);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Error SQL al listar auditorías");
            return StatusCode(500, new { mensaje = "Error al consultar auditorías." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error interno al listar auditorías");
            return StatusCode(500, new { mensaje = "Error interno del servidor." });
        }
    }

    // GET /api/Auditoria/1?idUsuarioAccionante=5
    [HttpGet("{idAuditoria:int}")]
    public async Task<IActionResult> ObtenerPorId(
        int idAuditoria, [FromQuery] int idUsuarioAccionante, CancellationToken ct)
    {
        if (idAuditoria <= 0 || idUsuarioAccionante <= 0)
            return BadRequest(new { mensaje = "Los IDs deben ser mayores que cero." });

        try
        {
            var registros = await ConsultarAsync(
                "dbo.sp_Auditoria_ObtenerPorId",
                new
                {
                    usuarioAccionante = new { idUsuario = idUsuarioAccionante },
                    datos = new { idAuditoria }
                }, ct);
            if (registros.Count == 0)
                return NotFound(new { mensaje = "No se encontró la auditoría solicitada." });
            return Ok(registros[0]);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Error SQL al consultar auditoría {IdAuditoria}", idAuditoria);
            return StatusCode(500, new { mensaje = "Error al consultar la auditoría." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error interno al consultar auditoría {IdAuditoria}", idAuditoria);
            return StatusCode(500, new { mensaje = "Error interno del servidor." });
        }
    }
}
