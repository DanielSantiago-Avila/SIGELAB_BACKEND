using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;

namespace SigelabBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LaboratorioController : ControllerBase
    {
        private readonly string _connectionString;

        public LaboratorioController(IConfiguration configuration)
        {
            _connectionString =
                configuration.GetConnectionString("SqlServerConn")
                ?? configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "No se configuró la conexión SQL Server. " +
                    "Verifica ConnectionStrings:SqlServerConn o DefaultConnection."
                );
        }

        // Manejo de errores de SQL Server.
        private IActionResult ErrorSql(SqlException ex)
        {
            return StatusCode(500, new
            {
                exito = false,
                mensaje = "No fue posible completar la operación en la base de datos."
            });
        }

        // GET: api/Laboratorio
        // Consultar todos los laboratorios.
        [HttpGet]
        public async Task<IActionResult> ObtenerLaboratorios()
        {
            try
            {
                var lista = new List<object>();

                await using (SqlConnection conn =
                    new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    const string query = @"
                        SELECT
                            id_laboratorio,
                            codigo,
                            nombre,
                            ubicacion,
                            descripcion,
                            activo,
                            capacidad
                        FROM dbo.Laboratorio
                        ORDER BY id_laboratorio DESC";

                    await using (SqlCommand cmd =
                        new SqlCommand(query, conn))
                    {
                        await using (SqlDataReader reader =
                            await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                lista.Add(new
                                {
                                    id_laboratorio = reader["id_laboratorio"],
                                    codigo = reader["codigo"]?.ToString() ?? "",
                                    nombre = reader["nombre"]?.ToString() ?? "",
                                    ubicacion = reader["ubicacion"] != DBNull.Value
                                        ? reader["ubicacion"].ToString()
                                        : "",
                                    descripcion = reader["descripcion"] != DBNull.Value
                                        ? reader["descripcion"].ToString()
                                        : "",
                                    activo = Convert.ToBoolean(reader["activo"]),
                                    capacidad = reader["capacidad"] != DBNull.Value
                                        ? Convert.ToInt32(reader["capacidad"])
                                        : 0
                                });
                            }
                        }
                    }
                }

                return Ok(lista);
            }
            catch (SqlException ex)
            {
                return ErrorSql(ex);
            }
        }

        // POST: api/Laboratorio
        // Crear un laboratorio.
        [HttpPost]
        public async Task<IActionResult> CrearLaboratorio(
            [FromBody] JsonElement jsonBody)
        {
            try
            {
                string jsonString = jsonBody.GetRawText();

                await using (SqlConnection conn =
                    new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    await using (SqlCommand cmd =
                        new SqlCommand("dbo.sp_laboratorio_crear", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;

                        cmd.Parameters.Add(
                            "@json",
                            SqlDbType.NVarChar,
                            -1
                        ).Value = jsonString;

                        await using (SqlDataReader reader =
                            await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                string mensaje = reader["mensaje"] != DBNull.Value
                                    ? reader["mensaje"].ToString()!
                                    : "Laboratorio creado correctamente.";

                                return Ok(new
                                {
                                    exito = true,
                                    mensaje
                                });
                            }
                        }
                    }
                }

                return Ok(new
                {
                    exito = true,
                    mensaje = "Laboratorio creado correctamente."
                });
            }
            catch (SqlException ex)
            {
                return ErrorSql(ex);
            }
            catch (JsonException)
            {
                return BadRequest(new
                {
                    exito = false,
                    mensaje = "El formato JSON enviado no es válido."
                });
            }
        }

        // PUT: api/Laboratorio
        // Actualizar un laboratorio existente.
        [HttpPut]
        public async Task<IActionResult> ActualizarLaboratorio(
            [FromBody] JsonElement jsonBody)
        {
            try
            {
                string jsonString = jsonBody.GetRawText();

                await using (SqlConnection conn =
                    new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    await using (SqlCommand cmd =
                        new SqlCommand("dbo.sp_laboratorio_actualizar", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;

                        cmd.Parameters.Add(
                            "@json",
                            SqlDbType.NVarChar,
                            -1
                        ).Value = jsonString;

                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                return Ok(new
                {
                    exito = true,
                    mensaje = "Laboratorio actualizado correctamente."
                });
            }
            catch (SqlException ex)
            {
                return ErrorSql(ex);
            }
            catch (JsonException)
            {
                return BadRequest(new
                {
                    exito = false,
                    mensaje = "El formato JSON enviado no es válido."
                });
            }
        }

        // PATCH: api/Laboratorio/{id}/estado
        // Cambiar el estado de un laboratorio.
        [HttpPatch("{id:int}/estado")]
        public async Task<IActionResult> CambiarEstado(
            int id,
            [FromBody] JsonElement jsonBody)
        {
            try
            {
                if (!jsonBody.TryGetProperty("activo", out var estadoJson) ||
                    (estadoJson.ValueKind != JsonValueKind.True &&
                     estadoJson.ValueKind != JsonValueKind.False))
                {
                    return BadRequest(new
                    {
                        exito = false,
                        mensaje = "Debes proporcionar el campo activo con un valor booleano."
                    });
                }

                bool nuevoEstado = estadoJson.GetBoolean();

                await using (SqlConnection conn =
                    new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    const string query = @"
                        UPDATE dbo.Laboratorio
                        SET activo = @activo
                        WHERE id_laboratorio = @id";

                    await using (SqlCommand cmd =
                        new SqlCommand(query, conn))
                    {
                        cmd.Parameters.Add(
                            "@activo",
                            SqlDbType.Bit
                        ).Value = nuevoEstado;

                        cmd.Parameters.Add(
                            "@id",
                            SqlDbType.Int
                        ).Value = id;

                        int filasAfectadas = await cmd.ExecuteNonQueryAsync();

                        if (filasAfectadas == 0)
                        {
                            return NotFound(new
                            {
                                exito = false,
                                mensaje = "No se encontró el laboratorio solicitado."
                            });
                        }
                    }
                }

                return Ok(new
                {
                    exito = true,
                    mensaje = "Estado del laboratorio actualizado correctamente."
                });
            }
            catch (SqlException ex)
            {
                return ErrorSql(ex);
            }
            catch (JsonException)
            {
                return BadRequest(new
                {
                    exito = false,
                    mensaje = "El formato JSON enviado no es válido."
                });
            }
        }
    }
}