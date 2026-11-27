# Herramientas de copia por cuenta

[English](ACCOUNT_BACKUP_ACCESS.md)

Las cuentas Standard y Admin autenticadas pueden exportar/importar su biblioteca
CSV, ejecutar una copia completa o incremental en el almacenamiento configurado,
consultar sus duplicados y carpetas, reparar sus URLs de imágenes y borrar
expresamente sus carpetas huérfanas. El administrador debe haber configurado el
almacenamiento del servidor. Exportar CSV no exige reparar URLs ni sincronizar antes.

La cuenta se obtiene de la autenticación. Ni `userId` en la URL ni `X-User-Id`
permiten elegir otra biblioteca o directorio. Limpiar caché como Standard afecta
solo a esa cuenta; la limpieza global, configuración del servidor y administración
de copias programadas siguen reservadas a Admin. No se ejecutan dos copias de una
misma cuenta a la vez.

Una imagen no disponible conserva su URL/archivo existente. Las descargas están
acotadas y no reciben credenciales; los destinos privados requieren una excepción
de ruta de imágenes configurada por el administrador. La falta de almacenamiento
es un problema de disponibilidad, no un motivo para dar permisos de administrador.
