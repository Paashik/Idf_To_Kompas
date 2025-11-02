using System.Collections.Generic;
using Idf2Kompas.Parsers; // для IdfGeometry.OutlineRings

namespace Idf2Kompas.Models
{
    /// <summary>Информация о контуре платы и толщине (из .BRD/.BOARD_OUTLINE).</summary>
    public sealed class BoardOutlineInfo
    {
        /// <summary>Единицы в HEADER: "MM" или "THOU".</summary>
        public string HeaderUnits { get; set; }

        /// <summary>Сырые строки блока .BOARD_OUTLINE (как есть из файла).</summary>
        public string RawBoardOutline { get; set; }

        /// <summary>Сырые строки блока .DRILLED_HOLES (как есть из файла).</summary>
        public string RawDrilledHoles { get; set; }

        /// <summary>Толщина платы в миллиметрах (если определена).</summary>
        public double ThicknessMm { get; set; }

        /// <summary>Опционально: уже распарсенные кольца контура (в мм) — можно заполнить позже.</summary>
        public IdfGeometry.OutlineRings Rings { get; set; }
    }

    /// <summary>Отверстие на плате.</summary>
    public sealed class IdfHole
    {
        public double X { get; set; }      // мм
        public double Y { get; set; }      // мм
        public double Dia { get; set; }    // мм
        public string Plating { get; set; } // "PTH"/"NPTH"/null
        public string Type { get; set; }    // "VIA","PIN","MTG","TOOL","OTHER"
    }

    public sealed class IdfPlacement
    {
        public string RefDes { get; set; }
        public string Comment { get; set; }   // Имя/комментарий ИЗ BRD
        public string Body { get; set; }     // Из BOM (модель/корпус в UI)
        public string PN { get; set; }        // Stock Code (BOM)
        public string FootprintFromBom { get; set; }
        public string FootprintFromIdf { get; set; }   // Посадочное ИЗ BRD
        public string PartNameFromIdf { get; set; }   // Имя ИЗ BRD
        public string ManufacturerPN { get; set; }         // Manufacturer P/N (BOM)
        public string Description { get; set; }            // Description (BOM)
        public string Type { get; set; }                   // Type (BOM)
       
        /// <summary>TOP/BOTTOM (если известно).</summary>
        public string Side { get; set; }

        /// <summary>X-координата (мм, система платы).</summary>
        public double X { get; set; }

        /// <summary>Y-координата (мм, система платы).</summary>
        public double Y { get; set; }

        /// <summary>Поворот, градусы, по часовой стрелке.</summary>
        public double RotDeg { get; set; }

        /// <summary>Z-координата посадочной плоскости (мм). По умолчанию 0 для TOP и −толщина для BOTTOM.</summary>
        public double Z { get; set; }

        /// <summary>Высота корпуса (мм) из EMP/базы, если доступна.</summary>
        public double? HeightFromEmp { get; set; }
    }

    public sealed class IdfBoard
    {
        /// <summary>Размещения компонентов.</summary>
        public List<IdfPlacement> Placements { get; } = new List<IdfPlacement>();

        /// <summary>Контур/толщина/сырые блоки.</summary>
        public BoardOutlineInfo Outline { get; set; } = new BoardOutlineInfo();

        /// <summary>Список отверстий.</summary>
        public List<IdfHole> Holes { get; set; } = new List<IdfHole>();
    }

    /// <summary>Опции парсинга/нормализации.</summary>
    public sealed class IdfParseOptions
    {
        /// <summary>Коррекция угла для нижней стороны: "AsIs" | "Negate" | "360Minus".</summary>
        public string BottomRotationMode { get; set; } = "AsIs";
    }
}
