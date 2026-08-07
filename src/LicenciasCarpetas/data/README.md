# data/

Carpeta de datos en ejecución. Todo lo que cae aquí está ignorado por git:

- `carpetas.db` — base SQLite con los casos importados (nombres y RUT reales).
- `exports/` — archivos `.xlsx` generados desde el dashboard.

La base se crea sola al arrancar la aplicación o al correr `--import`.
