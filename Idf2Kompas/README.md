Idf2Kompas
- Полный WinForms-проект (.NET Framework 4.7.2).
- Символ KOMPAS_API7 включён (Debug/Release).
- KompasService сначала пробует API7 (ProgID KOMPAS.Application.7/Kompas.Application.7), затем fallback к API5.
- Настройки: имена столбцов BOM, выбор источника имени модели (Body/Footprint/Comment/PN), порог сигнальных отверстий, флаги импорта отверстий, путь к библиотеке корпусов.
- Превью: колонка Model подсвечивается (зелёный — найдено в библиотеке, красный — нет).
- Ghbdtn/