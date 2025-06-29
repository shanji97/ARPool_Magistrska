using System.Collections.Generic;
using System.Linq;

public static class SemanticLabel
{
    //v72 and v74 constants.

    #region Room Structure

    public const string CEILING = "CEILING";
    public const string DOOR_FRAME = "DOOR_FRAME";

    public const string FLOOR = "FLOOR";
    public const string INVISIBLE_WALL_FACE = "INVISIBLE_WALL_FACE";

    public const string WALL_ART = "WALL_ART";

    public const string WALL_FACE = "WALL_FACE";

    public const string WINDOW_FRAME = "WINDOW_FRAME";

    public static List<string> GetRoomStructure()
    {
        return new List<string> { CEILING, DOOR_FRAME, FLOOR, INVISIBLE_WALL_FACE, WALL_ART, WALL_FACE, WINDOW_FRAME };
    }

    #endregion

    #region Room Contents

    public const string COUCH = "COUCH";
    public const string TABLE = "TABLE";

    public const string BED = "BED";

    public const string LAMP = "LAMP";
    public const string PLANT = "PLANT";

    public const string SCREEN = "SCREEN";

    public const string STORAGE = "STORAGE";

    public static List<string> GetRoomContents()
    {
        return new List<string> { COUCH, TABLE, BED, LAMP, PLANT, SCREEN, STORAGE };
    }

    #endregion

    #region  MeshObjectsa and unclassified objects
    public const string GLOBAL_MESH = "GLOBAL_MESH";
    public const string OTHER = "OTHER";
    #endregion

    public static List<string> GetCandidatesForCueBallDetection()
    {
        return new List<string> { LAMP, PLANT, OTHER, GLOBAL_MESH };
    }

    public static List<string> GetAll()
    {
        var all = new List<List<string>>(){
            GetRoomStructure(),
            GetRoomContents(),
            new() { GLOBAL_MESH, OTHER },
        };

        var combined = new List<string>();
        combined = all.Aggregate(combined, (current, list) => current.Concat(list).ToList());
        return combined;
    }
}