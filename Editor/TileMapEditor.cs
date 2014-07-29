using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.IO;

[CustomEditor(typeof(TileMap))]
public class TileMapEditor : Editor
{
    #region Class Variables
    enum State { Hover, BoxSelect }
    static Vector3[] rect = new Vector3[4];

    TileMap tileMap;
    FieldInfo undoCallback;
    bool editing;
    Matrix4x4 worldToLocal;
    State state;

    int cursorX;
    int cursorZ;
    int cursorClickX;
    int cursorClickZ;
    int planeY = 0;
    int currentY = 0;

    bool deleting;
    int direction;

    bool wireframeHidden;

    #endregion

    #region Inspector GUI

    public override void OnInspectorGUI()
    {
        //Get tilemap
        if (tileMap == null)
        {
            tileMap = (TileMap)target;
        }

        //Crazy hack to register undo
        if (undoCallback == null)
        {
            undoCallback = typeof(EditorApplication).GetField("undoRedoPerformed", BindingFlags.NonPublic | BindingFlags.Static);
            if (undoCallback != null)
            {
                undoCallback.SetValue(null, new EditorApplication.CallbackFunction(OnUndoRedo));
            }
        }

        //Toggle editing mode
        if (editing)
        {
            if (GUILayout.Button("Stop Editing"))
            {
                editing = false;
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Update All"))
                {
                    UpdateAll();
                }

                if (GUILayout.Button("Clear"))
                {
                    Clear();
                }

                EditorGUILayout.EndHorizontal();
            }
        }
        else if (GUILayout.Button("Edit TileMap"))
        {
            editing = true;
        }

        //Tile Size
        EditorGUI.BeginChangeCheck();
        float newTileSize = EditorGUILayout.FloatField("Tile Size", tileMap.tileSize);
        if (EditorGUI.EndChangeCheck())
        {
            RecordDeepUndo();
            tileMap.tileSize = newTileSize;
            UpdatePositions();
        }

        //Tile Prefab
        EditorGUI.BeginChangeCheck();
        Transform newTilePrefab = (Transform)EditorGUILayout.ObjectField("Tile Prefab", tileMap.tilePrefab, typeof(Transform), false);
        if (EditorGUI.EndChangeCheck())
        {
            RecordUndo();
            tileMap.tilePrefab = newTilePrefab;
        }

        //Tile Map
        EditorGUI.BeginChangeCheck();
        TileSet newTileSet = (TileSet)EditorGUILayout.ObjectField("Tile Set", tileMap.tileSet, typeof(TileSet), false);
        if (EditorGUI.EndChangeCheck())
        {
            RecordUndo();
            tileMap.tileSet = newTileSet;
        }

        //Tile Prefab selector
        if (tileMap.tileSet != null)
        {
            EditorGUI.BeginChangeCheck();
            string[] names = new string[tileMap.tileSet.prefabs.Length + 1];
            int[] values = new int[names.Length + 1];
            names[0] = tileMap.tilePrefab != null ? tileMap.tilePrefab.name : "";
            values[0] = 0;
            for (int i = 1; i < names.Length; i++)
            {
                names[i] = tileMap.tileSet.prefabs[i - 1] != null ? tileMap.tileSet.prefabs[i - 1].name : "";
                values[i] = i;
            }

            int index = EditorGUILayout.IntPopup("Select Tile", 0, names, values);
            if (EditorGUI.EndChangeCheck() && index > 0)
            {
                RecordUndo();
                tileMap.tilePrefab = tileMap.tileSet.prefabs[index - 1];
            }
        }

        //Selecting direction
        EditorGUILayout.BeginHorizontal(GUILayout.Width(60));
        EditorGUILayout.PrefixLabel("Direction");
        EditorGUILayout.BeginVertical(GUILayout.Width(20));
        GUILayout.Space(20);

        if (direction == 3)
        {
            GUILayout.Box("<", GUILayout.Width(20));
        }
        else if (GUILayout.Button("<"))
        {
            direction = 3;
        }

        GUILayout.Space(20);
        EditorGUILayout.EndVertical();
        EditorGUILayout.BeginVertical(GUILayout.Width(20));

        if (direction == 0)
        {
            GUILayout.Box("^", GUILayout.Width(20));
        }
        else if (GUILayout.Button("^"))
        {
            direction = 0;
        }

        if (direction == -1)
        {
            GUILayout.Box("?", GUILayout.Width(20));
        }
        else if (GUILayout.Button("?"))
        {
            direction = -1;
        }

        if (direction == 2)
        {
            GUILayout.Box("v", GUILayout.Width(20));
        }
        else if (GUILayout.Button("v"))
        {
            direction = 2;
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.BeginVertical(GUILayout.Width(20));
        GUILayout.Space(20);

        if (direction == 1)
        {
            GUILayout.Box(">", GUILayout.Width(20));
        }
        else if (GUILayout.Button(">"))
        {
            direction = 1;
        }

        GUILayout.Space(20);
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
    }

    #endregion

    #region Scene GUI

    void OnSceneGUI()
    {
        //Get tilemap
        if (tileMap == null)
        {
            tileMap = (TileMap)target;
        }

        //Toggle editing
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Tab)
        {
            editing = !editing;
            EditorUtility.SetDirty(target);
        }

        if (editing)
        {
            //Hide mesh
            HideWireframe(true);

            //Quit on tool change
            if (e.type == EventType.KeyDown)
            {
                switch (e.keyCode)
                {
                    case KeyCode.Q:
                    case KeyCode.W:
                    case KeyCode.E:
                    case KeyCode.R:
                        return;
                }
            }

            //Quit if panning or no camera exists
            if (Tools.current == Tool.View || (e.isMouse && e.button > 1) || Camera.current == null || e.type == EventType.ScrollWheel)
            {
                return;
            }

            //Quit if laying out
            if (e.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
                return;
            }

            //Update matrices
            Handles.matrix = tileMap.transform.localToWorldMatrix;
            worldToLocal = tileMap.transform.worldToLocalMatrix;

            //Draw axes
            Handles.color = Color.red;
            Handles.DrawLine(new Vector3(-tileMap.tileSize, tileMap.transform.position.y, 0), new Vector3(tileMap.tileSize, tileMap.transform.position.y, 0));
            Handles.DrawLine(new Vector3(0, tileMap.transform.position.y, -tileMap.tileSize), new Vector3(0, tileMap.transform.position.y, tileMap.tileSize));

            //Update mouse position
            Plane plane = new Plane(tileMap.transform.up, tileMap.transform.position);
            Ray ray = Camera.current.ScreenPointToRay(new Vector3(e.mousePosition.x, Camera.current.pixelHeight - e.mousePosition.y));
            float hit;
            if (!plane.Raycast(ray, out hit))
            {
                return;
            }

            Vector3 mousePosition = worldToLocal.MultiplyPoint(ray.GetPoint(hit));
            cursorX = Mathf.RoundToInt(mousePosition.x / tileMap.tileSize);
            cursorZ = Mathf.RoundToInt(mousePosition.z / tileMap.tileSize);

            //Update the state and repaint
            state = UpdateState();
            HandleUtility.Repaint();
            e.Use();
        }
        else
        {
            HideWireframe(false);
        }
    }

    void HideWireframe(bool hide)
    {
        if (wireframeHidden != hide)
        {
            wireframeHidden = hide;
            foreach (Renderer renderer in tileMap.transform.GetComponentsInChildren<Renderer>())
            {
                EditorUtility.SetSelectedWireframeHidden(renderer, hide);
            }
        }
    }

    #endregion

    #region Update state

    State UpdateState()
    {
        switch (state)
        {
            //Hovering
            case State.Hover:
                DrawGrid();
                DrawRect(cursorX, planeY, cursorZ, 1, 0, 1, Color.white, new Color(1, 1, 1, 0f));
                if (e.type == EventType.MouseDown && e.button < 2)
                {
                    cursorClickX = cursorX;
                    cursorClickZ = cursorZ;
                    deleting = e.button > 0;
                    return State.BoxSelect;
                }
                else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.UpArrow)
                {
                    // increase the height
                    planeY++;
                    currentY++;
                }
                else if (currentY > 0 && e.type == EventType.KeyDown && e.keyCode == KeyCode.DownArrow)
                {
                    // decrease the height if greater than 0
                    planeY--;
                    currentY--;
                }
                break;

            //Placing
            case State.BoxSelect:

                //Get the drag selection
                int x = Mathf.Min(cursorX, cursorClickX);
                int y = Mathf.Min(currentY, planeY);
                int z = Mathf.Min(cursorZ, cursorClickZ);
                int sizeX = Mathf.Abs(cursorX - cursorClickX) + 1;
                int sizeY = Mathf.Abs(currentY - planeY) + 1;
                int sizeZ = Mathf.Abs(cursorZ - cursorClickZ) + 1;

                if (e.type == EventType.KeyDown && e.keyCode == KeyCode.UpArrow)
                {
                    // increase the height
                    currentY++;
                }
                else if (currentY > 0 && e.type == EventType.KeyDown && e.keyCode == KeyCode.DownArrow)
                {
                    // decrease the height if greater than 0
                    currentY--;
                }

                //Draw the drag selection
                DrawRect(x, y, z, sizeX, sizeY, sizeZ, Color.white, deleting ? new Color(1, 0, 0, 0.2f) : new Color(0, 1, 0, 0.2f));

                //Finish the drag
                if (e.type == EventType.MouseUp && e.button < 2)
                {
                    if (deleting)
                    {
                        if (e.button > 0)
                        {
                            SetRect(x, y, z, sizeX, sizeY, sizeZ, null, direction);
                        }
                    }
                    else if (e.button == 0)
                    {
                        SetRect(x, y, z, sizeX, sizeY, sizeZ, tileMap.tilePrefab, direction);
                    }

                    planeY = currentY;
                    return State.Hover;
                }
                break;
        }

        return state;
    }

    void DrawGrid()
    {
        int gridSize = 5;
        float maxDist = Mathf.Sqrt(Mathf.Pow(gridSize - 1, 2) * 2) * 0.75f;
        for (int x = -gridSize; x <= gridSize; x++)
        {
            for (int z = -gridSize; z <= gridSize; z++)
            {
                Handles.color = new Color(1, 1, 1, 1 - Mathf.Sqrt(x * x + z * z) / maxDist);
                Vector3 p = new Vector3((cursorX + x) * tileMap.tileSize, 0, (cursorZ + z) * tileMap.tileSize);
                Handles.DotCap(0, p, Quaternion.identity, HandleUtility.GetHandleSize(p) * 0.02f);
            }
        }
    }

    void DrawRect(int x, int y, int z, int sizeX, int sizeY, int sizeZ, Color outline, Color fill)
    {
        Handles.color = Color.white;
        Vector3 min = new Vector3(x * tileMap.tileSize - tileMap.tileSize / 2, y * tileMap.tileSize - tileMap.tileSize / 2, z * tileMap.tileSize - tileMap.tileSize / 2);
        Vector3 max = min + new Vector3(sizeX * tileMap.tileSize, sizeY * tileMap.tileSize, sizeZ * tileMap.tileSize);
        rect[0].Set(min.x, min.y, min.z);
        rect[1].Set(max.x, min.y, min.z);
        rect[2].Set(max.x, min.y, max.z);
        rect[3].Set(min.x, min.y, max.z);
        Handles.DrawSolidRectangleWithOutline(rect, fill, outline);

        // we need to make this 3D representation if we've moved up or down
        if (sizeY != 0)
        {
            // draw the top of the box
            rect[0].Set(min.x, max.y, min.z);
            rect[1].Set(max.x, max.y, min.z);
            rect[2].Set(max.x, max.y, max.z);
            rect[3].Set(min.x, max.y, max.z);
            Handles.DrawSolidRectangleWithOutline(rect, fill, outline);

            // draw the sides of the box
            rect[0].Set(max.x, max.y, min.z);
            rect[1].Set(max.x, min.y, min.z);
            rect[2].Set(min.x, min.y, min.z);
            rect[3].Set(min.x, max.y, min.z);
            Handles.DrawSolidRectangleWithOutline(rect, fill, outline);

            rect[0].Set(max.x, max.y, max.z);
            rect[1].Set(max.x, min.y, max.z);
            rect[2].Set(min.x, min.y, max.z);
            rect[3].Set(min.x, max.y, max.z);
            Handles.DrawSolidRectangleWithOutline(rect, fill, outline);

            rect[0].Set(min.x, max.y, max.z);
            rect[1].Set(min.x, min.y, max.z);
            rect[2].Set(min.x, min.y, min.z);
            rect[3].Set(min.x, max.y, min.z);
            Handles.DrawSolidRectangleWithOutline(rect, fill, outline);

            rect[0].Set(max.x, max.y, max.z);
            rect[1].Set(max.x, min.y, max.z);
            rect[2].Set(max.x, min.y, min.z);
            rect[3].Set(max.x, max.y, min.z);
            Handles.DrawSolidRectangleWithOutline(rect, fill, outline);
        }
    }

    #endregion

    #region Modifying TileMap

    bool UpdateTile(int index)
    {
        //Destroy existing tile
        if (tileMap.instances[index] != null)
        {
            Undo.DestroyObjectImmediate(tileMap.instances[index].gameObject);
        }

        //Check if prefab is null
        if (tileMap.prefabs[index] != null)
        {
            //Place the tile
            Transform instance = (Transform)PrefabUtility.InstantiatePrefab(tileMap.prefabs[index]);
            instance.parent = tileMap.transform;
            instance.localPosition = tileMap.GetPosition(index);
            instance.localRotation = Quaternion.Euler(0, tileMap.directions[index] * 90, 0);
            tileMap.instances[index] = instance;
            wireframeHidden = false;
            return true;
        }
        else
        {
            //Remove the tile
            tileMap.hashes.RemoveAt(index);
            tileMap.prefabs.RemoveAt(index);
            tileMap.directions.RemoveAt(index);
            tileMap.instances.RemoveAt(index);
            return false;
        }
    }

    void UpdatePositions()
    {
        for (int i = 0; i < tileMap.hashes.Count; i++)
        {
            if (tileMap.instances[i] != null)
            {
                tileMap.instances[i].localPosition = tileMap.GetPosition(i);
            }
        }
    }

    void UpdateAll()
    {
        int x, y, z;
        for (int i = 0; i < tileMap.hashes.Count; i++)
        {
            tileMap.GetPosition(i, out x, out y, out z);
            SetTile(x, y, z, tileMap.prefabs[i], tileMap.directions[i]);
        }
    }

    void Clear()
    {
        RecordDeepUndo();
        int x, y, z;
        while (tileMap.hashes.Count > 0)
        {
            tileMap.GetPosition(0, out x, out y, out z);
            SetTile(x, y, z, null, 0);
        }
    }

    bool SetTile(int x, int y, int z, Transform prefab, int direction)
    {
        bool retVal = false;
        double hash = tileMap.GetHash(x, y, z);
        int index = tileMap.hashes.IndexOf(hash);

        if (index >= 0)
        {
            //Replace existing tile
            tileMap.prefabs[index] = prefab;
            if (direction < 0)
            {
                tileMap.directions[index] = Random.Range(0, 4);
            }
            else
            {
                tileMap.directions[index] = direction;
            }

            retVal = UpdateTile(index);
        }
        else if (prefab != null)
        {
            //Create new tile
            index = tileMap.prefabs.Count;
            tileMap.hashes.Add(hash);
            tileMap.prefabs.Add(prefab);
            if (direction < 0)
            {
                tileMap.directions.Add(Random.Range(0, 4));
            }
            else
            {
                tileMap.directions.Add(direction);
            }

            tileMap.instances.Add(null);
            retVal = UpdateTile(index);
        }

        return retVal;
    }

    void SetRect(int x, int y, int z, int sizeX, int sizeY, int sizeZ, Transform prefab, int direction)
    {
        RecordDeepUndo();
        for (int xx = 0; xx < sizeX; xx++)
        {
            for (int yy = 0; yy < sizeY; yy++)
            {
                for (int zz = 0; zz < sizeZ; zz++)
                {
                    SetTile(x + xx, y + yy, z + zz, prefab, direction);
                }
            }
        }
    }

    #endregion

    #region Undo handling

    void OnUndoRedo()
    {
        UpdatePositions();
    }

    void RecordUndo()
    {
        Undo.RecordObject(target, "TileMap Changed");
    }

    void RecordDeepUndo()
    {
        Undo.RegisterFullObjectHierarchyUndo(target, "TileMap Changed");
    }

    #endregion

    #region Properties

    Event e
    {
        get { return Event.current; }
    }

    #endregion

    #region Menu items

    [MenuItem("GameObject/Create Other/TileMap")]
    static void CreateTileMap()
    {
        GameObject obj = new GameObject("TileMap");
        obj.AddComponent<TileMap>();
    }

    [MenuItem("Assets/Create/TileSet")]
    static void CreateTileSet()
    {
        TileSet asset = ScriptableObject.CreateInstance<TileSet>();
        string path = AssetDatabase.GetAssetPath(Selection.activeObject);

        if (string.IsNullOrEmpty(path))
        {
            path = "Assets";
        }
        else if (Path.GetExtension(path) != "")
        {
            path = path.Replace(Path.GetFileName(AssetDatabase.GetAssetPath(Selection.activeObject)), "");
        }
        else
        {
            path += "/";
        }

        string assetPathAndName = AssetDatabase.GenerateUniqueAssetPath(path + "TileSet.asset");
        Debug.Log(assetPathAndName);
        AssetDatabase.CreateAsset(asset, assetPathAndName);
        AssetDatabase.SaveAssets();
        EditorUtility.FocusProjectWindow();
        Selection.activeObject = asset;
        asset.hideFlags = HideFlags.DontSave;
    }

    #endregion
}
