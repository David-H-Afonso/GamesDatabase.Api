# Idioma de la cuenta

[English](ACCOUNT_LANGUAGE.md)

`preferredLanguage` es una preferencia opcional: `en`, `es` o `null` si todavía no
se ha guardado un idioma para la cuenta. Las respuestas de perfil, lista de usuarios
y acceso incluyen el campo, también cuando es null. Las API anteriores lo omiten.

- Un administrador puede asignarlo en `POST /api/users` o cambiarlo mediante
  `PUT /api/users/{id}`.
- Un usuario puede cambiar su propia preferencia con la misma ruta PUT. Un usuario
  estándar no puede modificar otra cuenta.
- Omitir el campo o enviar null al editar conserva el valor actual. Otros códigos
  se rechazan. Prevalece la última elección explícita del usuario o administrador.

```json
{ "preferredLanguage": "es" }
```

La web carga la cuenta antes de abrir el tutorial. Su idioma guardado prevalece
sobre el que hubiera en el dispositivo. Apariencia guarda una nueva elección;
abrir la aplicación no vuelve a enviar una preferencia antigua del navegador.
Los cambios hechos desde otro sitio se leen al entrar, recargar o volver a la ventana.

Crear una cuenta en español también prepara sus catálogos iniciales en español.
Cambiar después el idioma no renombra catálogos existentes o personalizados.

`20261127141118_AddUserPreferredLanguage` añade una columna nullable a `user`.
No asigna un idioma a las cuentas existentes. Haz una copia de SQLite antes de
arrancar con el nuevo esquema. Para volver al binario anterior, conserva la columna
y los datos actuales; ese binario simplemente no la utiliza.
