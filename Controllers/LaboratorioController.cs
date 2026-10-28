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
        private readonly IConfiguration _configuration;

        public LaboratorioController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // GET: api/Laboratorio
        [HttpGet]
        public async Task<IActionResult> ObtenerLaboratorios()
        {
            var lista = new List<object>();
            string connectionString = _configuration.GetConnectionString("DefaultConnection");

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();
                string query = "SELECT id_laboratorio, codigo, nombre, ubicacion, descripcion, activo, capacidad FROM dbo.Laboratorio ORDER BY id_laboratorio DESC";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lista.Add(new
                            {
                                id_laboratorio = reader["id_laboratorio"],
                                codigo = reader["codigo"].ToString(),
                                nombre = reader["nombre"].ToString(),
                                ubicacion = reader["ubicacion"] != DBNull.Value ? reader["ubicacion"].ToString() : "",
                                descripcion = reader["descripcion"] != DBNull.Value ? reader["descripcion"].ToString() : "",
                                activo = Convert.ToBoolean(reader["activo"]),
                                capacidad = reader["capacidad"] != DBNull.Value ? Convert.ToInt32(reader["capacidad"]) : 0
                            });
                        }
                    }
                }
            }

            return Ok(lista);
        }

        // POST: api/Laboratorio
        [HttpPost]
        public async Task<IActionResult> CrearLaboratorio([FromBody] JsonElement jsonBody)
        {
            try
            {
                string connectionString = _configuration.GetConnectionString("DefaultConnection");
                string jsonString = jsonBody.GetRawText();

                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();
                    using (SqlCommand cmd = new SqlCommand("dbo.sp_laboratorio_crear", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@json", jsonString);

                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                return Ok(new
                                {
                                    exito = true,
                                    mensaje = reader["mensaje"]?.ToString() ?? "Laboratorio creado correctamente"
                                });
                            }
                        }
                    }
                }
                return Ok(new { exito = true });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { exito = false, mensaje = "Error en BD: " + ex.Message });
            }
        }

        // PUT: api/Laboratorio
        // Modifica un laboratorio existente
        [HttpPut]
        public async Task<IActionResult> ActualizarLaboratorio([FromBody] JsonElement jsonBody)
        {
            try
            {
                string connectionString = _configuration.GetConnectionString("DefaultConnection");
                string jsonString = jsonBody.GetRawText();

                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();
                    // Invoca el SP de actualización de tu BD
                    using (SqlCommand cmd = new SqlCommand("dbo.sp_laboratorio_actualizar", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@json", jsonString);

                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                return Ok(new { exito = true, mensaje = "Laboratorio actualizado correctamente" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { exito = false, mensaje = "Error al actualizar laboratorio: " + ex.Message });
            }
        }

        // PATCH: api/Laboratorio/{id}/estado
        // Cambia el estado (Activo/Inactivo)
        [HttpPatch("{id}/estado")]
        public async Task<IActionResult> CambiarEstado(int id, [FromBody] JsonElement jsonBody)
        {
            try
            {
                string connectionString = _configuration.GetConnectionString("DefaultConnection");
                bool nuevoEstado = jsonBody.GetProperty("activo").GetBoolean();

                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();
                    string query = "UPDATE dbo.Laboratorio SET activo = @activo WHERE id_laboratorio = @id";

                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@activo", nuevoEstado);
                        cmd.Parameters.AddWithValue("@id", id);

                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                return Ok(new { exito = true, mensaje = "Estado actualizado correctamente" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { exito = false, mensaje = "Error al cambiar estado: " + ex.Message });
            }
        }
    }
}