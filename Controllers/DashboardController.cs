
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;

namespace SigelabBackend.Controllers;

internal static class SqlJsonReportes
{
    public static async Task<List<List<Dictionary<string, object?>>>> EjecutarAsync(
        string connectionString,
        string procedimiento,
        object payload,
        CancellationToken cancellationToken)
    {
        var conjuntos = new List<List<Dictionary<string, object?>>>();

        await using var conexion = new SqlConnection(connectionString);
        await using var comando = new SqlCommand(procedimiento, conexion)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 60
        };

        comando.Parameters.Add("@json", SqlDbType.NVarChar, -1).Value =
            JsonSerializer.Serialize(payload);

        await conexion.OpenAsync(cancellationToken);

        await using var lector = await comando.ExecuteReaderAsync(cancellationToken);

        do
        {
            if (lector.FieldCount == 0)
                continue;

            var filas = new List<Dictionary<string, object?>>();

            while (await lector.ReadAsync(cancellationToken))
            {
                var fila = new Dictionary<string, object?>();

                for (int i = 0; i < lector.FieldCount; i++)
                {
                    fila[lector.GetName(i)] =
                        await lector.IsDBNullAsync(i, cancellationToken)
                            ? null
                            : lector.GetValue(i);
                }

                filas.Add(fila);
            }

            conjuntos.Add(filas);

        } while (await lector.NextResultAsync(cancellationToken));

        return conjuntos;
    }
}

[ApiController]
[Route("api/Dashboard")]
public class DashboardController : ControllerBase
{
    private readonly string _conexion;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(
        IConfiguration config,
        ILogger<DashboardController> logger)
    {
        _conexion = config.GetConnectionString("SqlServerConn")
            ?? config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "No existe una cadena de conexión SQL configurada.");

        _logger = logger;
    }

    private async Task<IActionResult> Consultar(
        string sp,
        CancellationToken ct)
    {
        try
        {
            var conjuntos = await SqlJsonReportes.EjecutarAsync(
                _conexion,
                sp,
                new { },
                ct);

            return Ok(conjuntos.Count == 1
                ? (object)conjuntos[0]
                : conjuntos);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Error SQL en {Procedimiento}", sp);

            return StatusCode(500, new
            {
                mensaje = "Error al consultar el dashboard."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error interno en {Procedimiento}", sp);

            return StatusCode(500, new
            {
                mensaje = "Error interno del servidor."
            });
        }
    }

    [HttpGet("resumen")]
    public Task<IActionResult> Resumen(CancellationToken ct) =>
        Consultar("dbo.sp_Dashboard_ObtenerResumen", ct);

    [HttpGet("equipos-por-estado")]
    public Task<IActionResult> EquiposPorEstado(CancellationToken ct) =>
        Consultar("dbo.sp_Reporte_EquiposPorEstado", ct);


    [HttpGet("prestamos-vencidos")]
    public Task<IActionResult> PrestamosVencidos(CancellationToken ct) =>
        Consultar("dbo.sp_Dashboard_ListarPrestamosVencidos", ct);
}

[ApiController]
[Route("api/Reporte")]
public class ReporteController : ControllerBase
{
    private readonly string _conexion;
    private readonly ILogger<ReporteController> _logger;

    public ReporteController(
        IConfiguration config,
        ILogger<ReporteController> logger)
    {
        _conexion = config.GetConnectionString("SqlServerConn")
            ?? config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "No existe una cadena de conexión SQL configurada.");

        _logger = logger;
    }

    private async Task<IActionResult> Consultar(
        string sp,
        object payload,
        CancellationToken ct)
    {
        try
        {
            var conjuntos = await SqlJsonReportes.EjecutarAsync(
                _conexion,
                sp,
                payload,
                ct);

            return Ok(conjuntos.Count == 1
                ? (object)conjuntos[0]
                : conjuntos);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Error SQL en {Procedimiento}", sp);

            return StatusCode(500, new
            {
                mensaje = "Error al generar el reporte."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error interno en {Procedimiento}", sp);

            return StatusCode(500, new
            {
                mensaje = "Error interno del servidor."
            });
        }
    }

    [HttpGet("prestamos-por-estado/{idEstadoPrestamo:int}")]
    public async Task<IActionResult> PrestamosPorEstado(
        int idEstadoPrestamo,
        CancellationToken ct)
    {
        if (idEstadoPrestamo <= 0)
            return BadRequest(new { mensaje = "ID de estado inválido." });

        return await Consultar(
            "dbo.sp_Reporte_PrestamosPorEstado",
            new { idEstadoPrestamo },
            ct);
    }

    [HttpGet("prestamos-entre-fechas")]
    public async Task<IActionResult> PrestamosEntreFechas(
        [FromQuery] DateOnly fechaInicio,
        [FromQuery] DateOnly fechaFin,
        CancellationToken ct)
    {
        if (fechaInicio == default ||
            fechaFin == default ||
            fechaInicio > fechaFin)
        {
            return BadRequest(new
            {
                mensaje = "Indique fechaInicio y fechaFin válidas (yyyy-MM-dd)."
            });
        }

        return await Consultar(
            "dbo.sp_Reporte_PrestamosEntreFechas",
            new
            {
                fechaInicio = fechaInicio.ToString("yyyy-MM-dd"),
                fechaFin = fechaFin.ToString("yyyy-MM-dd")
            },
            ct);
    }

    [HttpGet("prestamos-vencidos")]
    public Task<IActionResult> PrestamosVencidos(CancellationToken ct) =>
        Consultar(
            "dbo.sp_Reporte_PrestamosVencidos",
            new { },
            ct);

    [HttpGet("historial-equipo/{idEquipo:int}")]
    public async Task<IActionResult> HistorialEquipo(
        int idEquipo,
        CancellationToken ct)
    {
        if (idEquipo <= 0)
            return BadRequest(new { mensaje = "ID de equipo inválido." });

        return await Consultar(
            "dbo.sp_Reporte_HistorialEquipo",
            new { idEquipo },
            ct);
    }

    // CORREGIDO:
    // El SP espera:
    // {
    //   "usuarioAccionante": { "idUsuario": 5 },
    //   "datos": { "idSolicitante": 8 }
    // }
    [HttpGet("historial-solicitante/{idSolicitante:int}")]
    public async Task<IActionResult> HistorialSolicitante(
        int idSolicitante,
        [FromQuery] int idUsuarioAccionante,
        CancellationToken ct)
    {
        if (idSolicitante <= 0 || idUsuarioAccionante <= 0)
        {
            return BadRequest(new
            {
                mensaje = "El ID del solicitante y el ID del usuario accionante deben ser positivos."
            });
        }

        var payload = new
        {
            usuarioAccionante = new
            {
                idUsuario = idUsuarioAccionante
            },
            datos = new
            {
                idSolicitante
            }
        };

        return await Consultar(
            "dbo.sp_Reporte_HistorialSolicitante",
            payload,
            ct);
    }

    [HttpGet("equipos-por-estado/{idEstadoEquipo:int}")]
    public async Task<IActionResult> EquiposPorEstado(
        int idEstadoEquipo,
        CancellationToken ct)
    {
        if (idEstadoEquipo <= 0)
            return BadRequest(new { mensaje = "ID de estado inválido." });

        return await Consultar(
            "dbo.sp_Reporte_EquiposPorEstado",
            new { idEstadoEquipo },
            ct);
    }

    [HttpGet("equipos-por-laboratorio/{idLaboratorio:int}")]
    public async Task<IActionResult> EquiposPorLaboratorio(
        int idLaboratorio,
        CancellationToken ct)
    {
        if (idLaboratorio <= 0)
            return BadRequest(new { mensaje = "ID de laboratorio inválido." });

        return await Consultar(
            "dbo.sp_Reporte_EquiposPorLaboratorio",
            new { idLaboratorio },
            ct);
    }

    [HttpGet("historial-mantenimiento/{idEquipo:int}")]
    public async Task<IActionResult> HistorialMantenimiento(
    int idEquipo,
    [FromQuery] DateOnly fechaInicio,
    [FromQuery] DateOnly fechaFin,
    CancellationToken ct)
    {
        if (idEquipo <= 0 ||
            fechaInicio == default ||
            fechaFin == default ||
            fechaInicio > fechaFin)
        {
            return BadRequest(new
            {
                mensaje = "Indique idEquipo y fechas válidas (yyyy-MM-dd)."
            });
        }

        return await Consultar(
            "dbo.sp_Reporte_HistorialMantenimiento",
            new
            {
                datos = new
                {
                    idEquipo
                }
            },
            ct);
    }
}
