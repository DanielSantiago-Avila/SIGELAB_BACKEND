-- Ejecutar en la base SIGELAB.
-- ANTES: confirma que id_biometria es IDENTITY y que no hay otras columnas obligatorias:
--   EXEC sp_help 'dbo.BiometriaUsuario';

CREATE OR ALTER PROCEDURE dbo.sp_biometria_registrar
    @id_usuario          INT,
    @embedding_cifrado   VARBINARY(MAX),
    @modelo              NVARCHAR(100),
    @dimension_embedding INT,
    @calidad_promedio    DECIMAL(9,4)
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.Usuario WHERE id_usuario = @id_usuario AND activo = 1)
        THROW 50001, 'El usuario no existe o esta inactivo.', 1;

    IF EXISTS (SELECT 1 FROM dbo.BiometriaUsuario WHERE id_usuario = @id_usuario AND activo = 1)
        THROW 50002, 'El usuario ya tiene un rostro registrado.', 1;

    INSERT INTO dbo.BiometriaUsuario
        (id_usuario, embedding_cifrado, modelo, dimension_embedding,
         cantidad_muestras, calidad_promedio, activo, fecha_registro, fecha_actualizacion)
    VALUES
        (@id_usuario, @embedding_cifrado, @modelo, @dimension_embedding,
         1, @calidad_promedio, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_biometria_obtener
    @id_usuario INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (1) embedding_cifrado, modelo, dimension_embedding
    FROM dbo.BiometriaUsuario
    WHERE id_usuario = @id_usuario AND activo = 1
    ORDER BY fecha_registro DESC;
END
GO
