
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;

namespace SigelabBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PermisoTipoEquipoController : ControllerBase
    {
        private readonly string _connectionString;
        private readonly ILogger<PermisoTipoEquipoController> _logger;

        public PermisoTipoEquipoController(
            IConfiguration configuration,
            ILogger<PermisoTipoEquipoController> logger)
        {
            _connectionString =
                configuration.GetConnectionString("SqlServerConn")
                ?? configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "No se encontró la cadena de conexión SQL Server.");

            _logger = logger;
        }

        // Ejecutar procedimiento almacenado que devuelve registros
        private async Task<List<Dictionary<string, object?>>> ConsultarAsync(
            string procedimiento,
            object datos)
        {
            var resultados = new List<Dictionary<string, object?>>();

            string json = JsonSerializer.Serialize(datos);

            await using var conexion = new SqlConnection(_connectionString);
            await using var comando = new SqlCommand(procedimiento, conexion);

            comando.CommandType = CommandType.StoredProcedure;

            comando.Parameters.Add("@json", SqlDbType.NVarChar, -1)
                .Value = json;

            await conexion.OpenAsync();

            await using var reader = await comando.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var fila = new Dictionary<string, object?>();

                for (int i = 0; i < reader.FieldCount; i++)
                {
                    fila[reader.GetName(i)] =
                        await reader.IsDBNullAsync(i)
                            ? null
                            : reader.GetValue(i);
                }

                resultados.Add(fila);
            }

            return resultados;
        }

        // Ejecutar procedimiento almacenado que guarda datos
        private async Task<object?> EjecutarAsync(
            string procedimiento,
            object datos)
        {
            string json = JsonSerializer.Serialize(datos);

            await using var conexion = new SqlConnection(_connectionString);
            await using var comando = new SqlCommand(procedimiento, conexion);

            comando.CommandType = CommandType.StoredProcedure;

            comando.Parameters.Add("@json", SqlDbType.NVarChar, -1)
                .Value = json;

            await conexion.OpenAsync();

            await using var reader = await comando.ExecuteReaderAsync();

            var resultados = new List<Dictionary<string, object?>>();

            do
            {
                while (await reader.ReadAsync())
                {
                    var fila = new Dictionary<string, object?>();

                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        fila[reader.GetName(i)] =
                            await reader.IsDBNullAsync(i)
                                ? null
                                : reader.GetValue(i);
                    }

                    resultados.Add(fila);
                }
            }
            while (await reader.NextResultAsync());

            return resultados.Count > 0 ? resultados : null;
        }

        // =====================================================
        // 1. LISTAR TODOS LOS PERMISOS
        // GET /api/PermisoTipoEquipo
        // =====================================================
        [HttpGet]
        public async Task<IActionResult> Listar()
        {
            try
            {
                var resultado = await ConsultarAsync(
                    "dbo.sp_PermisoTipoEquipo_Listar",
                    new { });

                return Ok(resultado);
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex,
                    "Error SQL al listar permisos de tipo de equipo.");

                return StatusCode(500, new
                {
                    mensaje = "Error al consultar los permisos."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error interno al listar permisos.");

                return StatusCode(500, new
                {
                    mensaje = "Error interno del servidor."
                });
            }
        }

        // =====================================================
        // 2. LISTAR PERMISOS POR TIPO DE SOLICITANTE
        // GET /api/PermisoTipoEquipo/tipo-solicitante/1
        // =====================================================
        [HttpGet("tipo-solicitante/{idTipoSolicitante:int}")]
        public async Task<IActionResult> ListarPorTipoSolicitante(
            int idTipoSolicitante)
        {
            if (idTipoSolicitante <= 0)
            {
                return BadRequest(new
                {
                    mensaje = "El idTipoSolicitante debe ser mayor a cero."
                });
            }

            try
            {
                var resultado = await ConsultarAsync(
                    "dbo.sp_PermisoTipoEquipo_ListarPorTipoSolicitante",
                    new
                    {
                        idTipoSolicitante
                    });

                return Ok(resultado);
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex,
                    "Error SQL al consultar permisos del tipo {IdTipoSolicitante}.",
                    idTipoSolicitante);

                return StatusCode(500, new
                {
                    mensaje = "Error al consultar permisos del solicitante."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error interno al consultar permisos.");

                return StatusCode(500, new
                {
                    mensaje = "Error interno del servidor."
                });
            }
        }

        // =====================================================
        // 3. GUARDAR PERMISO
        // POST /api/PermisoTipoEquipo
        // =====================================================
        [HttpPost]
        public async Task<IActionResult> Guardar(
            [FromBody] GuardarPermisoTipoEquipoRequest request)
        {
            if (request == null ||
                request.UsuarioAccionante == null ||
                request.UsuarioAccionante.IdUsuario <= 0 ||
                request.IdTipoSolicitante <= 0 ||
                request.IdTipoEquipo <= 0)
            {
                return BadRequest(new
                {
                    mensaje = "Debe proporcionar un usuario accionante, " +
                              "tipo de solicitante y tipo de equipo válidos."
                });
            }

            try
            {
                var datos = new
                {
                    usuarioAccionante = new
                    {
                        idUsuario = request.UsuarioAccionante.IdUsuario
                    },
                    idTipoSolicitante = request.IdTipoSolicitante,
                    idTipoEquipo = request.IdTipoEquipo,
                    permitido = request.Permitido
                };

                var resultado = await EjecutarAsync(
                    "dbo.sp_PermisoTipoEquipo_Guardar",
                    datos);

                return Ok(new
                {
                    exito = true,
                    mensaje = "Procedimiento de guardado ejecutado correctamente.",
                    datos = resultado
                });
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex,
                    "Error SQL al guardar permiso de tipo de equipo.");

                return StatusCode(500, new
                {
                    exito = false,
                    mensaje = "Error al guardar el permiso."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error interno al guardar permiso.");

                return StatusCode(500, new
                {
                    exito = false,
                    mensaje = "Error interno del servidor."
                });
            }
        }
    }

    public class GuardarPermisoTipoEquipoRequest
    {
        public UsuarioAccionantePermiso? UsuarioAccionante { get; set; }

        public int IdTipoSolicitante { get; set; }

        public int IdTipoEquipo { get; set; }

        public bool Permitido { get; set; }
    }

    public class UsuarioAccionantePermiso
    {
        public int IdUsuario { get; set; }
    }
}
