# Security

## Reporting a vulnerability

Do not disclose credentials, tokens, private logs, database exports or exploit
details in a public issue or pull request.

Use GitHub's **Report a vulnerability** control when private reporting is
available. If it is unavailable, open a **Private security contact request** issue
without technical details or attachments so the maintainer can arrange a private
channel. A public contact request is not the vulnerability report itself.

Include the affected version/commit, deployment type, prerequisites, impact and
minimal reproduction steps in the private report. Use synthetic records rather
than personal libraries. Allow a coordinated fix before public disclosure.

## Self-hosted instances

Set a password on the bootstrap administrator before sharing access. Keep signing
keys, Steam credentials, databases, backups and exports on the server, outside the
frontend bundle and publicly served directories. Review TLS, origins, access
controls, rate limits and recovery for the actual deployment.

Removing a secret from Git does not revoke it. Rotate exposed credentials and
review retained tags, release assets and forge caches separately. Publishing an
image is not authorization to deploy it or restore a database.

## Español

No publiques credenciales, tokens, logs privados, exportaciones ni detalles de
explotación en issues o pull requests públicos.

Usa **Report a vulnerability** de GitHub cuando esté disponible el reporte privado.
Si no lo está, abre un issue **Private security contact request**, sin detalles
técnicos ni adjuntos, para acordar un canal privado con el mantenedor. Esa petición
de contacto no es el reporte de la vulnerabilidad.

Incluye versión/commit, tipo de despliegue, requisitos, impacto y reproducción
mínima en el canal privado. Utiliza registros ficticios, no bibliotecas personales,
y coordina la corrección antes de divulgarla.

Antes de compartir una instancia, protege el administrador inicial con contraseña.
Mantén claves, bases, copias y exportaciones en el servidor, fuera del frontend y
de las rutas públicas. Revisa TLS, orígenes, acceso, límites y recuperación reales.
Eliminar un secreto de Git no lo revoca: rota credenciales expuestas y revisa tags,
artefactos y cachés aparte. Publicar no autoriza desplegar ni restaurar datos.
