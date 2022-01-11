using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
[CustomEditor(typeof(WayPath)), CanEditMultipleObjects]
public sealed class WayPathHelper : Editor
{
    Texture2D tex;
    int selectedIndex = -1;
    void OnEnable()
    {
        tex = new Texture2D(1, 1); tex.SetPixel(0, 0, Color.white); tex.Apply();
    }
    void OnDestroy() => Object.DestroyImmediate(tex);
    private void OnSceneGUI()
    {
        WayPath waypath = (WayPath)target; //Get WayPath

        List<WayPath.Waypoint> points = waypath.points;
        //if (points.Count < 2) return;//Failsafe for if less than enough points found
            
        #region Scene Waypath Info GUI
        Handles.BeginGUI();
        GUILayout.BeginArea(new Rect(10, 10, 250, 200));

        Rect rect = EditorGUILayout.BeginVertical();
        GUIStyle style = new GUIStyle();
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.MiddleLeft;
        GUI.color = Color.yellow;
        GUI.Box(rect, GUIContent.none);
        GUI.color = Color.white;
        GUILayout.BeginHorizontal();
        //GUILayout.FlexibleSpace();
        GUILayout.Label(
                $"Reverse         : {waypath.IsReverse}" +
                $"\nWaiting         : {waypath.isWaiting}" +
                (points.Count > 0 ? (
                $"\nPrevious        : {waypath.PrevIndex}" +
                $"\nCurrent         : {waypath.Index}" +
                $"\nNext            : {waypath.NextIndex}"
                ) : "" ) +
                $"\nTimestamp       : {waypath.TimeSinceLastWP}" +
                $"\nCatch Up Time   : {waypath.CatchUpTime}" +
                $"\nCurTime         : {waypath.CurrentTime}" +
                $"\nAccurTime       : {waypath.AccurateCurrentTime}" +
                (points.Count > 0 ? (
                $"\nTime on point   : {waypath.InterpolTime - waypath.CatchUpTime}" +
                $"\nMove time       : {waypath.MoveTime}"
                ) : "" ) +
                $"\nGame Time       : {Time.time}");
        //GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUI.backgroundColor = Color.red;
        GUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
        GUILayout.EndArea();
        Handles.EndGUI();
        #endregion
        
        GUIStyle infoSty = new GUIStyle();
        infoSty.fontStyle = FontStyle.Normal;
        infoSty.fontSize = 13;
        infoSty.richText = true;
        GUIStyle indexSty = new GUIStyle();
        indexSty.fontStyle = FontStyle.Bold;
        indexSty.fontSize = 12;
        indexSty.richText = true;
        indexSty.alignment = TextAnchor.MiddleCenter;
        indexSty.contentOffset = new Vector2(0, -25f);
        string GetColorHex(Color32 c) => "#" + c.r.ToString("X2") + c.g.ToString("X2") + c.b.ToString("X2");

        float Hue = 0f;

        //Code for each point in WayPath
        List<Vector3> dotlines = new List<Vector3>(); //Prepare an array to use DottedLines()
        for (int l = 0; l < points.Count; l++) //Draw curves and labels for each point
        {
            WayPath.Waypoint curPt = points[l];//Get current point and store it
            bool isSelect = selectedIndex == l;
            if (!isSelect)
            {
                float size = HandleUtility.GetHandleSize(waypath.Relative(curPt)) * 0.12f;
                Handles.color = Color.white;
                Quaternion rot = SceneView.lastActiveSceneView.camera.transform.rotation;
                Vector3 posi = waypath.Relative(curPt);
                bool picked = Handles.Button(waypath.Relative(curPt), rot, size, size, Handles.RectangleHandleCap); //Select Waypoint button
                Handles.color = Color.red;
                bool deleted = Handles.Button(posi + ((rot * Vector3.right) + (rot * Vector3.up)) * size * 1.4f, rot, size / 3, size / 3, Handles.RectangleHandleCap); //Delete Waypoint button
                if(deleted) { Undo.RecordObject(waypath, "Deleted Waypoint"); points.RemoveAt(l); l = -1; continue; }
                if (picked) selectedIndex = l;
            }
            WayPath.Waypoint nexPt = points[(l + 1) % points.Count];  //Same for next
            Handles.color = Color.HSVToRGB((Hue += 0.05f) + (curPt.useLinear ? 0.5f : 0), 1, 1) * new Color(1f,1f,1f, isSelect ? 1f : 0.3f);
            Vector3 pos;
            //Code for position handle per point
            if (isSelect) 
            { 
                EditorGUI.BeginChangeCheck();
                pos = Handles.DoPositionHandle(waypath.Relative(curPt), Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(waypath, "Move Waypoint");
                    curPt.point = waypath.Irrelative(pos);
                    waypath.points[l] = curPt;
                }
            }
            //Label
            string colorHex = GetColorHex(Handles.color);
            Handles.Label(waypath.Relative(curPt), $"<color=white>{l}</color>", indexSty);
            Handles.Label(waypath.Relative(curPt), $"<color={colorHex}>Point {l}\n{(Vector3)curPt}{curPt.waitTime.ToString() + "s"}</color>", infoSty); //Label wait times
            Handles.ScaleSlider(5f, Vector3.zero, Vector3.up, Quaternion.identity, 10f, 0.01f);
            Vector3[] arr = curPt.Vectors;
            ArrayUtility.Add(ref arr, nexPt);
            if((l != points.Count - 1) || waypath.isLoop)Handles.Label(waypath.Relative(WayPath.BezierCalc(0.5f,arr)), $"<color={colorHex}>{curPt.time.ToString() + "s"}</color>", infoSty);//Label times, unless not a loop
            Vector3[] lineArrReuse; //Reuse the same array
            Vector3[] LineArr() //Used to build the array for Handles.DottedLines()
            {
                lineArrReuse = new Vector3[curPt.Vectors.Length * 2];
                for (int i = 0; i < curPt.Vectors.Length - 1; i++)
                { int ind = i * 2; lineArrReuse[ind] = waypath.Relative(curPt.Vectors[i]); lineArrReuse[ind + 1] = waypath.Relative(curPt.Vectors[i + 1]); } //Make vector pairs
                lineArrReuse[lineArrReuse.Length - 2] = waypath.Relative(curPt.Vectors[curPt.Vectors.Length - 1]); lineArrReuse[lineArrReuse.Length - 1] = waypath.Relative(nexPt); //Make last pair
                return lineArrReuse;
            }
            if (curPt.Type != WayPath.Waypoint.CurveType.Linear && (waypath.isLoop ? true : l < points.Count - 1) ) //Beginning of tangent handles
            {
                //dotlines.AddRange(LineArr());
                Handles.DrawDottedLines(LineArr(), 3f);
                if (isSelect)
                {
                    EditorGUI.BeginChangeCheck();
                    pos = Handles.DoPositionHandle(waypath.Relative(curPt.startTangent), Quaternion.identity);
                    Handles.Label(pos, pos.ToString());
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(waypath, "Move start tangent");
                        curPt.startTangent = waypath.Irrelative(pos);
                        waypath.points[l] = curPt;
                    }
                }
                if(curPt.Type == WayPath.Waypoint.CurveType.Cubic)
                {
                    if (isSelect)
                    {
                        EditorGUI.BeginChangeCheck();
                        pos = Handles.DoPositionHandle(waypath.Relative(curPt.endTangent), Quaternion.identity);
                        Handles.Label(pos, pos.ToString());
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(waypath, "Move end tangent");
                            curPt.endTangent = waypath.Irrelative(pos);
                            waypath.points[l] = curPt;
                        }
                    }
                    if(waypath.points[l].TotalPointTime != 0)Handles.DrawBezier(waypath.Relative(curPt), waypath.Relative(nexPt), waypath.Relative(curPt.startTangent), waypath.Relative(curPt.endTangent), Handles.color, tex, 1.5f);
                }
                else if(waypath.points[l].TotalPointTime != 0)
                {
                    Handles.DrawBezier(waypath.Relative(curPt), waypath.Relative(nexPt),
                        waypath.Relative(curPt + (2 / 3f * (curPt.startTangent - curPt))),
                        waypath.Relative(nexPt + (2 / 3f * (curPt.startTangent - nexPt))), Handles.color, tex, 1.5f
                        );
                }
            }
        }
        for (int i = 0; i < dotlines.Count; i++) dotlines[i] = waypath.Relative(dotlines[i]);
        //Handles.DrawDottedLines(dotlines.ToArray(), 4f);
        //if (waypath.isLoop) { Handles.Label(waypath.Relative((points[0].point + points[points.Length - 1].point) / 2), points[points.Length - 1].time.ToString()); }
        
    }
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        
        
        WayPath waypath = (WayPath)target;

        List<WayPath.Waypoint> WPList = waypath.points;
        Color normColor = GUI.backgroundColor;
        GUILayout.Label($"Total number of points : {WPList.Count}");
        GUILayout.Label($"Total Animation Time  : {waypath.TotalAnimTime}");
        for (int i = 0; i < WPList.Count; i++) //Create Waypoint's Inspector GUI
        {
            GUI.backgroundColor = i == waypath.Index ? Color.cyan : (i == waypath.NextIndex ? Color.green: normColor);
            WayPath.Waypoint point = WPList[i];
            bool focusPt = point.foldedInInspector;
            point.foldedInInspector = EditorGUILayout.BeginFoldoutHeaderGroup(point.foldedInInspector, $"{i} : {point.point} : {point.Type} : W {point.waitTime} : T {point.time}");
            focusPt ^= point.foldedInInspector;
            if (focusPt) SceneView.lastActiveSceneView.Frame(new Bounds(point.point, Vector3.one * 10f));
            if (point.foldedInInspector)
            {
                point.Type = (WayPath.Waypoint.CurveType)EditorGUILayout.EnumPopup(WPList[i].Type);
                point.point = EditorGUILayout.Vector3Field("Point", point.point);
                if (WPList[i].Type == WayPath.Waypoint.CurveType.Quadratic) //Build fields for tangents
                {
                    point.startTangent = EditorGUILayout.Vector3Field("Tangent", point.startTangent);
                }
                if (WPList[i].Type == WayPath.Waypoint.CurveType.Cubic)
                {
                    point.startTangent = EditorGUILayout.Vector3Field("Start Tangent", point.startTangent);
                    point.endTangent = EditorGUILayout.Vector3Field("End Tangent", point.endTangent);
                }
                point.waitTime = EditorGUILayout.FloatField("Wait Time", point.waitTime);
                point.waitTime = point.waitTime < 0 ? 0 : point.waitTime; 
                point.time = EditorGUILayout.FloatField("Time", point.time);
                point.time = point.time < 0f ? 0f : point.time;
                point.useLinear = !EditorGUILayout.Toggle(!point.useLinear);
                if (GUILayout.Button("Delete waypoint"))
                {
                    WPList.RemoveAt(i);
                    i--;
                    continue;
                }
            }
            WPList[i] = point;
            EditorGUILayout.EndFoldoutHeaderGroup();
            
            
        }
        GUI.backgroundColor = normColor;
        if (!waypath.isLoop) GUILayout.Label("Some of the last Waypoint's time will not be used in an unlooped cycle");
        if (GUILayout.Button("Add new point at current position", GUILayout.Height(40)))
        {        
            Vector3 pos = waypath.transform.localPosition;
            SerializedProperty array = serializedObject.FindProperty("points");
            array.InsertArrayElementAtIndex(waypath.points.Count);
            
            SerializedProperty point = array.GetArrayElementAtIndex(waypath.points.Count);
            SerializedProperty vecArray = point.FindPropertyRelative("vectors");
            vecArray.InsertArrayElementAtIndex(0);
            SerializedProperty vec = vecArray.GetArrayElementAtIndex(0);
            vec.vector3Value = pos;
            SerializedProperty time = point.FindPropertyRelative("time");
            time.floatValue = waypath.points.Count == 0 ? 
                1f
                :
                Vector3.Distance(pos, waypath.points[waypath.points.Count - 1].point);
            
            serializedObject.ApplyModifiedProperties();
            WayPath.Waypoint w = waypath.points[waypath.points.Count - 1];
            w.Type = 0;
            waypath.points[waypath.points.Count - 1] = w;
            selectedIndex = waypath.points.Count - 1;
        }
        if (GUILayout.Button("Place Object At Starting Point", GUILayout.Height(40)))
        {
            if (waypath.points.Count != 0)
            {
                Undo.RecordObject(waypath.transform, $"Reset to start position of waypoints{waypath.name}");
                waypath.transform.position = waypath.GetLocationAtTime(waypath.timeOffset);
            }
            else Debug.LogWarning("No points are set"); //Remove later
        }
        if(GUILayout.Button("Fix time of first waypoint"))
        {
            if (waypath.points.Count <= 1) Debug.LogWarning("Not enough waypoints to calculate");
            else
            {
                float time = Vector3.Distance(waypath.points[0],waypath.points[waypath.points.Count-1]);
                serializedObject.FindProperty("points").GetArrayElementAtIndex(0).FindPropertyRelative("time").floatValue = time;
                serializedObject.ApplyModifiedProperties();
            }
        }
        EditorGUILayout.FloatField("Value", 1f);
        EditorGUILayout.IntField("Index", 1);
        if(GUILayout.Button("Multiply time of each point by value")) { Debug.LogWarning("Sorry, this does nothing yet"); }
        if(GUILayout.Button("Multiply time of point index by value")) { Debug.LogWarning("Sorry, this does nothing yet"); }
        if(GUILayout.Button("Set time of each point equal to distance")) { Debug.LogWarning("Sorry, this does nothing yet"); }
        if(GUILayout.Button("Multiply wait time of each point by value")) { Debug.LogWarning("Sorry, this does nothing yet"); }
        GUI.backgroundColor = waypath.isActive ? Color.red : Color.green;
        if (GUILayout.Button(waypath.isActive ? "Stop" : "Play", GUILayout.Height(35f))) waypath.Toggle();
        GUI.backgroundColor = new Color32(198, 101, 240, 255);
        if (GUILayout.Button("Reverse")) { waypath.Reverse(true); }
        GUI.backgroundColor = Color.yellow;
        if (GUILayout.Button((waypath.StopAtNext ? "Continue" : "Stop") + " at next point")) { waypath.StopAtNext = !waypath.StopAtNext; }
        GUI.backgroundColor = new Color32(76, 195, 224, 255);
        if (GUILayout.Button("Skip Wait")) { waypath.SkipWait(); }

        if (GUILayout.Button("Clear Points"))
        {
            serializedObject.FindProperty("points").ClearArray();
            serializedObject.ApplyModifiedProperties();
        }
        SceneView.RepaintAll();
    }
    bool testActive = false;
    
}
