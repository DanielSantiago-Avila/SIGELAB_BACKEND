var builder = WebApplication.CreateBuilder(args);

// Agregar servicios para los Controladores
builder.Services.AddControllers();

// Configurar CORS para permitir peticiones desde Flutter
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFlutter",
        policy => policy.AllowAnyOrigin()
                        .AllowAnyMethod()
                        .AllowAnyHeader());
});

var app = builder.Build();

// Activar Middleware
app.UseCors("AllowFlutter");
app.UseAuthorization();

// Mapear Controladores (ej. AuthController)
app.MapControllers();

app.Run();