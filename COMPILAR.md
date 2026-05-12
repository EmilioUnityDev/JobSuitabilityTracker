# Guía de compilación e instalación — Job Suitability Tracker

## Requisitos previos

| Herramienta | Dónde conseguirla |
|---|---|
| Visual Studio 2022 (Community es gratuito) | https://visualstudio.microsoft.com/ |
| Carga de trabajo **.NET desktop development** | Instalar desde el Visual Studio Installer |
| Oxygen Not Included (en Steam) | Ya lo tienes |

---

## Paso 1 — Ajustar la ruta de instalación de ONI

Abre `Source/JobSuitabilityTracker.csproj` con un editor de texto y cambia la línea:

```xml
<ONI_PATH>C:\Program Files (x86)\Steam\steamapps\common\OxygenNotIncluded</ONI_PATH>
```

por la ruta real de tu instalación. Para encontrarla:
- Steam → Biblioteca → clic derecho en Oxygen Not Included → **Propiedades** → **Archivos locales** → **Examinar**.

---

## Paso 2 — Compilar

1. Abre Visual Studio 2022.
2. Selecciona **Abrir un proyecto o solución** y abre `Source/JobSuitabilityTracker.csproj`.
3. En el menú superior elige **Compilar → Compilar solución** (o `Ctrl+Shift+B`).
4. Si todo va bien verás `Compilación: 1 correcta`. La DLL se generará en `JobSuitabilityTracker/JobSuitabilityTracker.dll`.

> **Error común**: "No se puede encontrar el archivo de referencia". Significa que la ruta `ONI_PATH` es incorrecta. Verifica que la carpeta `OxygenNotIncluded_Data\Managed\` exista dentro de la ruta que pusiste.

---

## Paso 3 — Instalar el mod

1. Localiza la carpeta de mods de ONI:
   ```
   %AppData%\..\LocalLow\Klei\Oxygen Not Included\mods\local\
   ```
   (pega eso en el explorador de Windows o en la barra de direcciones).

2. Crea una carpeta llamada `JobSuitabilityTracker` dentro de `local\`.

3. Copia **toda** la carpeta del mod (los dos archivos imprescindibles):
   ```
   JobSuitabilityTracker/
   ├── mod.yaml
   └── JobSuitabilityTracker.dll   ← generada por Visual Studio
   ```

4. Arranca ONI, ve al menú de **Mods** y activa **Job Suitability Tracker**.

---

## Paso 4 — Verificar que funciona

1. Carga una partida donde el logro "Job Suitability" esté en progreso.
2. Cuando el juego muestre la notificación de progreso del logro verás, debajo de los contadores existentes, la lista de duplicantes con ✓ o ✗.

---

## Si el mod no detecta el logro (lista vacía o no aparece)

El ID del logro en el código podría ser diferente al que tenemos configurado por defecto. Para encontrar el correcto:

### Método rápido: mirar el log del juego

1. Arranca ONI con el mod activo.
2. Abre el archivo de log:
   ```
   %AppData%\..\LocalLow\Klei\Oxygen Not Included\Player.log
   ```
3. Busca las líneas que empiezan por `[JobSuitabilityTracker]`. Verás algo como:
   ```
   [JobSuitabilityTracker] ID: 'JobSuitability'   | Nombre: Job Suitability
   [JobSuitabilityTracker] ID: 'SomeOtherAchievement' | Nombre: ...
   ```
4. Copia el ID correcto y edítalo en el archivo `Source/JobSuitabilityTracker.cs`, línea:
   ```csharp
   public static string AchievementID = "JobSuitability";
   ```
5. Vuelve a compilar e instalar.

### Método avanzado: dnSpy (para curiosos)

dnSpy es un decompilador gratuito que te permite ver el código del juego.

1. Descarga dnSpy desde: https://github.com/dnSpy/dnSpy/releases
2. Abre con dnSpy el archivo:
   ```
   [carpeta ONI]\OxygenNotIncluded_Data\Managed\Assembly-CSharp.dll
   ```
3. En el árbol de la izquierda busca: `Database` → `ColonyAchievements`
4. Busca el logro "Job Suitability" y mira el valor de su campo `staticID` o el primer parámetro de su constructor.
5. Usa ese valor como `AchievementID` en el código.

También puedes buscar el campo que almacena el progreso por duplicante. Busca la clase del requisito dentro del logro y mira qué campos de tipo `Dictionary<int, int>` tiene. Si el nombre del campo no es encontrado automáticamente, puedes forzarlo editando `FindField<T>` para buscar por nombre explícito.

---

## Estructura de archivos final

```
[mods/local/]
└── JobSuitabilityTracker/
    ├── mod.yaml                    ← metadatos del mod
    ├── JobSuitabilityTracker.dll   ← DLL compilada
    └── Source/                     ← código fuente (opcional, no necesario para jugar)
        ├── JobSuitabilityTracker.cs
        └── JobSuitabilityTracker.csproj
```
