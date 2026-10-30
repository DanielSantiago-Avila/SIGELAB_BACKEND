
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;

namespace SigelabBackend.Controllers;

[ApiController]
[Route("api/ConsultasSolicitante")]
public class ConsultasSolicitanteController : ControllerBase
{
    private readonly string _conexion;
    private readonly ILogger<ConsultasSolicitanteController> _logger;

    public ConsultasSolicitanteController(
        IConfiguration config,
        ILogger<ConsultasSolicitanteController> logger)
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
            var conjuntos = new List<List<Dictionary<string, object?>>>();

            await using var conexion = new SqlConnection(_conexion);

            await using var comando = new SqlCommand(sp, conexion)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };

            comando.Parameters.Add(
                "@json",
                SqlDbType.NVarChar,
                -1
            ).Value = JsonSerializer.Serialize(payload);

            await conexion.OpenAsync(ct);

            await using var lector = await comando.ExecuteReaderAsync(ct);

            do
            {
                if (lector.FieldCount == 0)
                    continue;

                var filas = new List<Dictionary<string, object?>>();

                while (await lector.ReadAsync(ct))
                {
                    var fila = new Dictionary<string, object?>();

                    for (int i = 0; i < lector.FieldCount; i++)
                    {
                        fila[lector.GetName(i)] =
                            await lector.IsDBNullAsync(i, ct)
                                ? null
                                : lector.GetValue(i);
                    }

                    filas.Add(fila);
                }

                conjuntos.Add(filas);

            } while (await lector.NextResultAsync(ct));

            return Ok(conjuntos.Count == 1
                ? (object)conjuntos[0]
                : conjuntos);
        }
        catch (SqlException ex)
        {
            _logger.LogError(
                ex,
                "Error SQL en {Procedimiento}",
                sp);

            return StatusCode(500, new
            {
                mensaje = "Error al consultar préstamos del solicitante."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error interno en {Procedimiento}",
                sp);

            return StatusCode(500, new
            {
                mensaje = "Error interno del servidor."
            });
        }
    }

    // IMPORTANTE:
    // idUsuarioAccionante se recibe por query solo para las pruebas.
    // Antes de producción debe obtenerse de la identidad autenticada.

    // GET /api/ConsultasSolicitante/mis-prestamos?idUsuarioAccionante=8
    [HttpGet("mis-prestamos")]
    public async Task<IActionResult> MisPrestamos(
        [FromQuery] int idUsuarioAccionante,
        CancellationToken ct)
    {
        if (idUsuarioAccionante <= 0)
        {
            return BadRequest(new
            {
                mensaje = "ID de usuario inválido."
            });
        }

        var payload = new
        {
            usuarioAccionante = new
            {
                idUsuario = idUsuarioAccionante
            }
        };

        return await Consultar(
            "dbo.sp_Prestamo_ListarMisPrestamos",
            payload,
            ct);
    }

    // GET /api/ConsultasSolicitante/mis-prestamos/1?idUsuarioAccionante=8
    // CORREGIDO: idPrestamo debe estar dentro de "datos".
    [HttpGet("mis-prestamos/{idPrestamo:int}")]
    public async Task<IActionResult> DetallePropio(
        int idPrestamo,
        [FromQuery] int idUsuarioAccionante,
        CancellationToken ct)
    {
        if (idPrestamo <= 0 || idUsuarioAccionante <= 0)
        {
            return BadRequest(new
            {
                mensaje = "IDs inválidos."
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
                idPrestamo
            }
        };

        return await Consultar(
            "dbo.sp_Prestamo_ObtenerDetallePropio",
            payload,
            ct);
    }
}
