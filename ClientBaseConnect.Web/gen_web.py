# -*- coding: utf-8 -*-
from pathlib import Path

root = Path(__file__).parent / "wwwroot"
(root / "css").mkdir(parents=True, exist_ok=True)
(root / "js").mkdir(parents=True, exist_ok=True)

(root / "index.html").write_text(
    """<!DOCTYPE html>
<html lang="ru">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover" />
  <meta name="theme-color" content="#0E0E14" />
  <title>Клиенты+ — личный кабинет</title>
  <link rel="stylesheet" href="css/app.css" />
</head>
<body>
  <div id="app" class="app">
    <section id="screen-login" class="screen screen--active">
      <motion-not-needed></motion-not-needed>
    </section>
  </div>
  <script src="js/config.js"></script>
  <script src="js/app.js"></script>
</body>
</html>""".replace(
        "<motion-not-needed></motion-not-needed>",
        '<motion-not-needed></motion-not-needed>',
    ),
    encoding="utf-8",
)
