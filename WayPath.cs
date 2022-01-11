using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using UnityEngine.SceneManagement;
/// <summary>
/// A cool script to create EZ moving platforms with just a few buttons and alot of functions
/// </summary>
public sealed class WayPath : MonoBehaviour
{
    #region PathDict Manipulation
    static Dictionary<string, PointList> PathDict = new Dictionary<string, PointList>();
    /// <summary>
    /// Clones the current used Waypath path under the name of the current key with the Gameobject's InstanceID appended
    /// </summary>
    public void CloneCurrentPath()
    {
        if (path != null)
        {
            PointList copy = path.Copy();
            key += gameObject.GetInstanceID().ToString();
            PathDict.Add(key, copy);
        }
        else throw new System.MissingMemberException($"WayPath in gameobject {name} does not have a path to clone");
    }

    /// <param name="name">The name of the new key that the path will take instead. </param>
    public void CloneCurrentPath(string name)
    {
        if (path != null)
        {
            PointList copy = path.Copy();
            PathDict.Add(name, copy);
        }
        else throw new System.MissingMemberException($"WayPath in gameobject {name} does not have a path to clone");
    }
    string key;
    PointList path;
    public PointList Path => path;
    ///<summary>Determines if waypath moves interpolates using time or speed. Not implemented, not likely.</summary>
    public bool useSpeed;

    public class PointList
    {
        public ObservableCollection<Waypoint> points = new ObservableCollection<Waypoint>() { }; //Maybe should create an float list as a buffer for position look-up 
        public PointList Copy() => (PointList)this.MemberwiseClone();
        public float TotalTime
        {
            get
            {
                float tot = 0;
                for (int l = 0; l < points.Count; l++) tot += points[l].TotalPointTime;
                return tot;
            }
        }
    }
    #endregion
    /// <summary>
    /// Holds the start time of the level. Using some Unity voodoo magic(in the form of RuntimeInitializeOnLoadMethod), this is set to everytime a Scene is loaded. 
    /// You can use -name of method- and it automatically set this Waypath to the time the Scene was loaded. Keep in mind if you used an additive LoadSceneMode, this will probably not work.
    /// </summary>
    public static float levelStartTime { get; private set; }
    /// <summary>
    /// Determines if the class should carry out it's function
    /// </summary>
    public bool isActive { get; private set; } = true;
    /// <summary>
    /// Used to show gizmo's in the editor
    /// </summary>
    public bool showGizmo = true;
    public bool showModel = true;
    public bool setForward = true;
    /// <summary>
    /// Used to show Debug.Log() in the editor
    /// </summary>
    public bool showDebugInfo = false; //Reinforce
    public bool ResetOnDeath = true;
    public bool isLoop = false;
    /// <summary>
    /// Future Implementation. Dictates if the Waypath will use the remapping of curves for interpolation.
    /// </summary>
    public bool useRemap = true;
    public Color32 gizmoColor = Color.blue;
    public float timeOffset = 0;
    float animationStartStamp = 0; //Used for short calculation
    float animationStopStamp = 0; //Used for when stopping
    [HideInInspector]
    public List<Waypoint> points = new List<Waypoint>() { }; //Maybe should create an float list as a buffer for position look-up 
    public int Length { get => points.Count; }
    public bool AllowPositionChange = false;
    public Vector3 CurveDerivative { get; private set; } = Vector3.zero;
    public Vector3 Additive { get; private set; }
    Vector3 _additive = Vector3.zero;
    Vector3 lastPos;
    /// <summary>
    /// The default influence value
    /// </summary>
    public float globalInfluence;
    ///<summary>Used by Waypath to determine what variables needs their buffers updated, such as TotalAnimTime. Not yet Implemented.</summary>
    public int UpdateMask { get; }
    /// <summary>
    /// The maximum delta the platform can put on any object stuck to it
    /// </summary>    
    public bool IsReverse { get; private set; } = false; //Used as reference when looping
    Coroutine routine;
    ///<summary>Represents the current waypoint index</summary>
    public int Index { get; private set; } = 0; //ADD SET FOR WHEN SETTING INDEX
    ///<summary>Retrieves the internal 'timeStamp' value that is set to Time.time upon reaching a waypoint and dictates interpolation. 
    ///Because it reflects interpolation, this value represents the last time that a change of points should have occurred.</summary>
    public float TimeSinceLastWP => (isActive ? Time.time : animationStopStamp) - timeStamp;
    float timeStamp;
    Rigidbody2D rb; bool rbExists;
    public float influenceLimit;
    ///<summary>Is the object current waiting at a waypoint?</summary>
    public bool isWaiting { get; private set; } = true;
    public MaskOverride masks;
    ///<summary>A list of all objects currently to be snapped to the platform</summary>
    Dictionary<Transform, int> snappedObjects = new Dictionary<Transform, int>();
    /// <summary>
    /// How many colliders up can the platform do influence? Not implemented.
    /// </summary>
    public byte recursiveLimit = 1;
    public Waypoint this[int index] { get => points[index]; set => points[index] = value; } //ADD THING
    /// <summary>
    /// Called when checkpoint is reached, returning the reached index and the calling waypath
    /// </summary>
    public event System.Action<int, WayPath> ReachedWayPoint;
    #region Calculated Attr
    /// <summary>
    /// Gets total time to complete a single cycle
    /// </summary>
    public float TotalAnimTime //Find a way to avoid constant calc
    {
        get
        {
            float tot = 0;
            for (int l = 0; l < points.Count; l++) tot += points[l].TotalPointTime;
            if (!isLoop)
            {
                tot *= 2;
                tot -= points[points.Count - 1].TotalPointTime + points[points.Count - 1].time + points[0].waitTime; //yes, this is correct, 'future me'.
            }
            return tot;
        } }
    /// <summary>
    /// Gets total time of points added together
    /// </summary>
    public float TotalTime //MAKE BUFFER IN FUTURE
    {
        get
        {
            float tot = 0;
            for (int l = 0; l < points.Count; l++) tot += points[l].TotalPointTime;
            return tot;
        } }
    /// <summary>
    /// Returns the current interpolation amount in between current and next Waypoints WITH interpolation curving
    /// </summary>
    public float Interpolation { get
        {
            float timeEval = ((isActive ? Time.time : animationStopStamp) - timeStamp - Current.waitTime) / (IsReverse ? Next.time : Current.time);
            bool useLinear = Current.useLinear == Next.useLinear ? Current.useLinear : timeEval < 0.5 ? Current.useLinear : Next.useLinear;
            return useLinear ? timeEval : (Mathf.Cos((Mathf.Max(timeEval, 0) * Mathf.PI) + Mathf.PI) + 1) / 2f;
        }
    }
    /// <summary>
    /// Returns the current interpolation amount in between current and next Waypoints WITHOUT interpolation curving
    /// </summary>
    public float TrueInterpolation
    {
        get => ((isActive ? Time.time : animationStopStamp) - timeStamp - Current.waitTime) / (IsReverse ? Next.time : Current.time);
    }
    
    ///<summary>The current Waypoint object, or point interpolation will start from.</summary>
    public Waypoint Current { get => points[Index]; }
    ///<summary>The next Waypoint object, or point interpolation will end at.</summary>
    public Waypoint Next { get => points[NextIndex]; }
    ///<summary>The total interpolation time between Current and Next. This differs from Current.totalPointTime as it considers reversed cycles</summary>
    public float InterpolTime => IsReverse ? Current.waitTime + Next.time : Current.TotalPointTime ;
    ///<summary>Returns the next point index.</summary>
    //Takes the index, adds or subtracts according to if in reverse, and adds the length of the point array to avoid negative indexes upon modulo
    public int NextIndex => (Index + points.Count + (IsReverse ? -1 : 1)) % points.Count;
    ///<summary>Returns the previous point index</summary>
    public int PrevIndex => (Index + points.Count + (IsReverse ? 1 : -1)) % points.Count;
    #endregion
    public float CatchUpTime { get; private set; } = 0f;
    public bool StopAtNext { get; set; } = false;
    public bool ReverseAtNext { get; set; } = false;
    // /////////////////////////////// TANGENT METHODS ////////////////////////////////////
    public enum Tangent { Quadratic, Cubic0, Cubic1 }
    /// <summary>
    /// Gets the Tangent of Waypoint at index. Unlike getting the tangent directly, this method can also return the theoretical tangent if the curve degree doesn't match. 
    /// <para>
    /// For example, should the given waypoint at 'index' have a Quadratic curve and you request the first tangent of a Cubic curve, it will return the first tangent of a Cubic curve that is the equivalent to the Quadratic curve 
    /// </para>
    /// </summary>
    /// <param name="index">The index of the Waypoint</param>
    /// <param name="tangent">The vector that will recieve the tangent, if possible</param>
    /// <returns>Whether a tangent could be returned. All curves have their higher degree equivalents, but not the other way around, therefore an answer can't always be found</returns>
    public bool GetTangent(int index, Tangent type, out Vector3 tangent)
    {
        if(index > points.Count - 1) throw new System.IndexOutOfRangeException("Call to GetTangent with index parameter out of range");
        tangent = Vector3.zero;
        Waypoint start = points[index];
        Waypoint end = points[(index + 1) % points.Count];
        if (type == Tangent.Quadratic && start.Type == Waypoint.CurveType.Cubic) return false; //Return false if impossible conversion
        else if(type == Tangent.Quadratic) //Requested Tangent is Only Quadratic
        {
            switch (start.Type)
            {
                case Waypoint.CurveType.Linear:
                    tangent = Vector3.Lerp(start, end, 0.5f);
                    return true;
                case Waypoint.CurveType.Quadratic:
                    tangent = start.startTangent;
                    return true;
            }
        }
        else //Requested Tangent is Only Cubic
        {
            switch (start.Type)
            {
                case Waypoint.CurveType.Linear:
                    tangent = Vector3.Lerp(start, end, (type == Tangent.Cubic0 ? 1f : 2f) / 3f);
                    return true;
                case Waypoint.CurveType.Quadratic:
                    Waypoint kind = type == Tangent.Cubic0 ? start : end; //Simple variable substitution according to which tangent it is
                    tangent = kind + (2 / 3f * (start.startTangent - kind));
                    return true;
                case Waypoint.CurveType.Cubic:
                    tangent = type == Tangent.Cubic0 ? start.startTangent : start.endTangent;
                    return true;
            }
            
        }
        throw new System.Exception("how????"); //Technically can't happen
    }



    // /////////////////////////////// TANGENT METHODS ////////////////////////////////////
    
    ///<summary>Actually moves the index to the next waypoint</summary>
    void NextSequence() { Index = NextIndex; if (isLoop) return; if (Index == 0) IsReverse = false; else if (Index == (points.Count - 1)) IsReverse = true; }
    #region Unity Messages 
    private void Awake()
    {
        lastPos = transform.localPosition;
        if (Mathf.Abs(timeOffset) > 0) SetAtTime(timeOffset);
        else
        {
            timeStamp = animationStartStamp = animationStopStamp = Time.time;
            rbExists = (rb = GetComponent<Rigidbody2D>()) != null;
            transform.localPosition = points[0];
            lastPos = transform.localPosition;
            if (isActive) { isActive = false; Play(); }
        }
    }
    void FixedUpdate()
    {
        Additive += transform.localPosition - lastPos;
        Vector3 result;
        if (!isActive | isWaiting) return; //If not active, do not spend resources doing anything. ADD FUNCTION FOR DYNAMIC RB'S
        
        Vector3[] v = new Vector3[(int)(IsReverse ? Next.Type : Current.Type) + 2];
        (IsReverse ? Next : Current).Vectors.CopyTo(v, 0);
        v[v.Length - 1] = IsReverse ? Current: Next;
        Vector3 der;
        result = BezierCalc(IsReverse ? 1 - Interpolation : Interpolation, out der, v);
        CurveDerivative = IsReverse ? -der : der;
        Vector3 diff = result - transform.localPosition;
        if (rbExists) rb.MovePosition(result + Additive);
        else transform.localPosition = result + Additive;
        if (setForward) transform.forward = CurveDerivative;
        lastPos = transform.localPosition;                            //I think there might be something wrong here
        foreach (Transform t in snappedObjects.Keys) 
        {
            if (t) t.Translate(diff, Space.World);
            else snappedObjects.Remove(t); 
        }
        
    }
    void OnCollisionEnter(Collision c) { if (snappedObjects.ContainsKey(c.transform)) snappedObjects[c.transform]++; else snappedObjects.Add(c.transform, 1);  }
    void OnCollisionExit(Collision c) { if (--snappedObjects[c.transform] < 1) snappedObjects.Remove(c.transform); }
    private void OnDrawGizmos()
    {
        Color alpha = new Color(1, 1, 1, 0.6f);
        float Hue = 0f;
        if (points.Count >= 2 && showGizmo)
        {
            //Draw Lines, with linear lines colored half more hue
            int k = 0;
            Gizmos.color = Color.HSVToRGB((Hue += 0.05f) + (points[points.Count - 1].useLinear ? 0.5f : 0), 1, 1) * alpha;
            if (isLoop) Gizmos.DrawLine(Relative(points[0].point), Relative(points[points.Count - 1].point));
            while (k < points.Count - 1)
            {
                Gizmos.color = (Mathf.Approximately(points[k].time,0f) ? Color.clear : Color.HSVToRGB((Hue += 0.05f) + (points[k].useLinear ? 0.5f : 0f), 1 ,1)) * alpha;
                Gizmos.DrawLine(Relative(points[k].point), Relative(points[k + 1].point));
                k++;
            }
            //Draw fancy Bezier visuals
            int degree = (int)(IsReverse ? Next.Type : Current.Type) + 2;
            Vector3[] pts = new Vector3[degree];
            (IsReverse ? Next : Current).Vectors.CopyTo(pts, 0);
            pts[pts.Length - 1] = IsReverse ? Current : Next;
            float t = IsReverse ? 1 - Interpolation : Interpolation;
            Gizmos.color = new Color(1, 1, 1, 0.5f);
            for (int i = degree; i > 1; i--)
                for (int w = 0; w < i - 1; w++)
                {Gizmos.DrawLine(Relative(pts[w]), Relative(pts[w+1])); pts[w] = Vector3.Lerp(pts[w], pts[w + 1], t); }
            //Draw mesh that represents time offset
            MeshFilter mesh;
            if (!(mesh = GetComponent<MeshFilter>())) return;
            Gizmos.color = gizmoColor;
            if (showModel) Gizmos.DrawWireMesh(mesh.sharedMesh, 0, GetLocationAtTime(timeOffset), transform.rotation, transform.localScale);
            else Gizmos.DrawWireCube(GetLocationAtTime(timeOffset), transform.localScale);
        }
    }
    private void OnValidate()
    {
        //if (points.Count == 2 & isLoop == true) Debug.LogWarning($"isLooped will be switched to {isLoop = false} due to being less than 3 points");
    }
    #endregion
    #region Exposed WayPath Controls 
    ///<summary>Reverses the cycle. Not implemented.</summary><param name="invertWait">Should any current wait also be inverted? Set this to true for true reversal.</param>
    ///<returns>Returns true if a reverse could be performed. Reversals when waiting at either of the end points of an unlooped cycle are not possible.</returns>
    public bool Reverse(bool invertWait) //ADD LOGIC TO ADJUST CURRENTTIME PROPERTY
    {
        //If wait should not be inverted, isLoop is off, and currently waiting at one of the endpoints, then do NOTHING.
        if (!invertWait && !isLoop && isWaiting && (Index == 0 || Index == points.Count - 1)) return false;
        float actualTime = isActive ? Time.time : animationStopStamp; //Accounts for stopped cycles
        if (!isWaiting)//Logic if currently moving between two points
        {
            StopCoroutine(routine);
            float moveTime = MoveTime;
            float elaspedMoveTime = (actualTime)- (timeStamp + Current.waitTime);
            timeStamp = (actualTime - (moveTime - elaspedMoveTime)) - Next.waitTime;
            NextSequence();
            if(isActive) routine = StartCoroutine(Timer(elaspedMoveTime));
            if (isLoop ||  (Index != 0 && Index != points.Count - 1)) IsReverse = !IsReverse;

        }
        else if(invertWait)//Logic if currently waiting and wait should be invert
        {
            float elaspedWaitTime = actualTime - timeStamp;
            StopCoroutine(routine);
            timeStamp = actualTime - (Current.waitTime - elaspedWaitTime);
            if(isActive) routine = StartCoroutine(Timer(elaspedWaitTime));
            if (isLoop || (Index != 0 && Index != points.Count - 1)) IsReverse = !IsReverse;
            else Debug.Log("Did not reverse");
        }
        else //Logic if currently waiting and wait should NOT invert
        {
            IsReverse = !IsReverse;
        }
        return true;
    } //Be sure to remember reversal while at index 0 or points.Count is redundant

    ///<summary>Stops the current animation. If already stopped, nothing happens.</summary>
    public void Stop()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) isActive = false;
#endif
        if (!isActive) return;
        if(routine != null) StopCoroutine(routine);
        animationStopStamp = Time.time;
        isActive = false;
    }

    ///<summary>Resumes the current animation. If already playing, nothing happens.</summary>
    public void Play()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) isActive = true;
#endif
        if (isActive) return;
        timeStamp += Time.time - animationStopStamp;
        animationStartStamp += Time.time - animationStopStamp;
        isActive = true;
        routine = StartCoroutine(Timer((isWaiting ? Current.waitTime : InterpolTime) - (Time.time - timeStamp)));
    }

    ///<summary>Stops/Plays the current animation. Not yet Implemented</summary>
    public void Toggle() { if (isActive) Stop(); else Play(); }
    /// <summary>
    /// Skips the wait time at the current point, if it's currently waiting. If not waiting, nothing happens.
    /// </summary>
    /// <returns>Returns whether a skip could be made. Essentially the equivalent to isWaiting.</returns>
    public bool SkipWait()
    {
        if (!isWaiting) return false;
        timeStamp -= MoveTime - TimeSinceLastWP;
        
        return true;
    }

    ///<summary>Adds an object to be influenced by the delta of the platform. Not yet Implemented.</summary>
    public void AddSnappedObject() { throw new System.NotImplementedException(); }

    #endregion
    ///<summary>Handles switching to a new point upon reaching the end</summary>
    void ChangePoint()
    {
        if (!isWaiting)// If NOT waiting, switch to the new point
        {
            do
            {
                CatchUpTime = (Time.time - timeStamp) - InterpolTime; //Used to account for minimal time loss/gain
                timeStamp = Time.time - CatchUpTime;
                NextSequence();
                if (Index == 0) animationStartStamp = timeStamp;
                //transform.localPosition = Current;  FIX FIX FIX
            } while (Mathf.Approximately(InterpolTime, 0f) || CatchUpTime > InterpolTime);
            if (showDebugInfo)
                Debug.Log(
                    $"isReverse {IsReverse} / " +
                    $"Cur {Index} / " +
                    $"Nex {NextIndex} " +
                    $"\nTime difference : {CatchUpTime} / " +
                    $"CurTime : {CurrentTime} / " +
                    $"AccurTime : {AccurateCurrentTime} / " +
                    $"Offset : {CatchUpTime} / " +
                    $"Time on point {InterpolTime - CatchUpTime}");
            float timer = ((isWaiting = !Mathf.Approximately(Current.waitTime, 0f)) ? Current.waitTime : MoveTime) - CatchUpTime;
            routine = StartCoroutine(Timer(timer));
            if(showDebugInfo)Debug.Log($"{(isWaiting ? "Waiting at " : "Moving to ")} " +
                $"{(isWaiting ? Index : NextIndex)} " +
                $"in {timer}");
            /* ^^^ Expansion of above statement ^^^
            if (Mathf.Approximately(Current.waitTime, 0f)) //If there is no wait, go straight to moving
            {
                isWaiting = false;
                routine = StartCoroutine(Timer(MoveTime - CatchUpTime));
            }
            else
            {
                isWaiting = true;
                routine = StartCoroutine(Timer(Current.waitTime - CatchUpTime));
            }
            */
            if (StopAtNext) { StopAtNext = false; Stop(); }
            int i = 0;
            
        }
        else //If waiting, continue to move AT point or move to next point if no move time(snap point)
        {
            if (isWaiting = Mathf.Approximately(0f, MoveTime)) { if (showDebugInfo) Debug.Log($"Snapping to {NextIndex}"); ChangePoint(); } //Snap to the next point if there is no move time, while apropiately setting isWaiting
            else //Move
            {
                CatchUpTime = Time.time - (timeStamp + Current.waitTime);
                routine = StartCoroutine(Timer(MoveTime - CatchUpTime));
                if(showDebugInfo) Debug.Log($"Moving to {NextIndex} in {MoveTime - CatchUpTime}");
            }
        }
    }
    ///<summary>Used for calling ChangePoint at set 'time'.</summary>
    IEnumerator Timer(float time)
    {
        yield return new WaitForSeconds(time);
        ChangePoint();
    }
    /// <summary>
    /// Set to a specific time relative the start of animation (excluding timeOffset). Use AccurTime if you wish to add upon current time
    /// </summary>
    public void SetAtTime(float time) 
    {
        //Index Evaluation
        if(routine != null) StopCoroutine(routine); //Stop current coroutine
        if (IsReverse) { time *= -1; time += points[0].waitTime; }
        time = Mathf.Repeat(time, TotalAnimTime);                                  //Avoid class fields with locals
        animationStartStamp = Time.time - time;
        float tot = TotalTime - (isLoop ? 0 : points[points.Count - 1].time); //Get total time minus the last point if it's a loop, as it isn't used 
        if (IsReverse = !isLoop & time >= tot)
        { time += points[points.Count - 1].waitTime - tot; } //Minus the first half of time if time is greater than it. Set reverse in the meantime
        int iter = 0;
        while (true) //Loop through each point
        {
            float added = IsReverse ? points[points.Count - (iter + 1)].waitTime + points[points.Count - (iter + 2)].time : points[iter].TotalPointTime;
            if (time < added) break; else time -= added; iter++; //If time remaining is greater than the total time on the point, then finally break
        }
        Index = IsReverse ? (points.Count - (iter + 1)) : (iter);
        //Location evaluation
        Waypoint current = points[Index];
        Waypoint next = points[NextIndex];
        float timeEval = (time - current.waitTime) / (IsReverse ? next.time : current.time);
        bool useLinear = current.useLinear == next.useLinear ? current.useLinear : timeEval < 0.5 ? current.useLinear : next.useLinear;
        float interpol = useLinear ? timeEval : (Mathf.Cos((Mathf.Max(timeEval, 0) * Mathf.PI) + Mathf.PI) + 1) / 2f;
        Vector3[] arr = new Vector3[(IsReverse ? next : current).Vectors.Length + 1];
        (IsReverse ? next : current).Vectors.CopyTo(arr, 0); arr[arr.Length - 1] = IsReverse ? current : next;
        Vector3 dir;
        Vector3 result = Relative(BezierCalc(IsReverse ? 1 - interpol : interpol, out dir, arr));
        CurveDerivative = dir;
        //Action
        if (rb) rb.MovePosition(result); else transform.localPosition = result + Additive;
        lastPos = transform.localPosition;
        isWaiting = time < current.waitTime;
        timeStamp = Time.time - time;
        time = isWaiting ? (Current.waitTime - time) : (MoveTime - (time - Current.waitTime));
        if (isActive) routine = StartCoroutine(Timer(time)); else animationStopStamp = Time.time;
    }
    /// <summary>
    /// This function calculates a bezier curve with any degree using De Casteljau's algorithm.
    /// </summary>
    /// <param name="t">It probably stands for 'Time'</param>
    /// <param name="points">Self-explanatory</param>
    /// <param name="derivative">Derivative of the curve at the given time</param>
    /// <returns>If you don't know what this should return, then you probably shouldn't use it.</returns>
    public static Vector3 BezierCalc(float t, out Vector3 derivative, params Vector3[] points)
    {
        if (points.Length < 2) throw new System.InvalidOperationException($"BezierCalc() not given enough points. Given {points.Length} points");
        int degree = points.Length;
        for (int i = degree; i > 1; i--)
            for (int k = 0; k < i - 1; k++)
                points[k] = Vector3.Lerp(points[k], points[k + 1], t);
        derivative = points[1] - points[0];
        return points[0];
    }
    public static Vector3 BezierCalc(float t, params Vector3[] points)
    {
        if (points.Length < 2) throw new System.InvalidOperationException($"BezierCalc() not given enough points. Given {points.Length} points");
        int degree = points.Length;
        for (int i = degree; i > 1; i--)
            for (int k = 0; k < i - 1; k++)
                points[k] = Vector3.Lerp(points[k], points[k + 1], t);
        return points[0];
    }
    public void ResetToStart() => SetAtTime(timeOffset);
    /// <summary>
    /// Gets the estimated location of the game object at a certain time relative to start of animation. Not yet implemented
    /// </summary>
    public Vector3 GetLocationAtTime(float time, out Vector3 derivative) //how tf this works????
    { 
        //Reuse algorithm used in SetAtTime
        if (IsReverse) { time *= -1; time += points[0].waitTime; }
        time = Mathf.Repeat(time, TotalAnimTime);
        bool isReverse;                                    //Avoid class fields with locals
        float tot = TotalTime - (isLoop ? 0 : points[points.Count - 1].time); //Get total time minus the last point if it's a loop, as it isn't used 
        if (isReverse = !isLoop & time >= tot) 
        { time += points[points.Count - 1].waitTime - tot; } //Minus the first half of time if time is greater than it. Set reverse in the meantime
        int iter = 0;
        while (true) //Loop through each point
        {
            float added = isReverse ? points[points.Count - (iter + 1)].waitTime + points[points.Count - (iter + 2)].time : points[iter].TotalPointTime;
            if (time < added) break; else time -= added; iter++; //If time remaining is greater than the total time on the point, then finally break
        }
        iter = isReverse ? (points.Count - (iter + 1)) : (iter);
        Waypoint Current = points[iter];
        Waypoint next = points[(iter + points.Count + (isReverse ? -1 : 1)) % points.Count];
        float timeEval = (time - Current.waitTime) / (isReverse ? next.time : Current.time); 
        bool useLinear = Current.useLinear == next.useLinear ? Current.useLinear : timeEval < 0.5 ? Current.useLinear : next.useLinear;
        float interpol = useLinear ? timeEval : (Mathf.Cos((Mathf.Max(timeEval, 0) * Mathf.PI) + Mathf.PI) + 1) / 2f;
        Vector3[] arr =  new Vector3[(isReverse ? next : Current).Vectors.Length + 1];
        (isReverse ? next : Current).Vectors.CopyTo(arr,0); arr[arr.Length - 1] = isReverse ? Current : next;
        Vector3 result = BezierCalc(isReverse ? 1 - interpol: interpol, out derivative,arr);
        return transform.parent != null ? transform.parent.TransformPoint(result) : result;
    }
    public Vector3 GetLocationAtTime(float time) //yes im lazy leave me alone
    {
        return GetLocationAtTime(time, out _);
    }
    /// <summary>
    /// Like GetLocationAtTime, but takes into consideration other WayPath's up the hierarchy. Not yet implemented, not likely either.
    /// </summary>
    public Vector3 GetLocationAtTimeRelativeToHierarchy() => throw new System.NotImplementedException();
    /// <summary>
    /// This property uses a short calculation and a variable set to Time.time every time the animation loops, very slightly inaccurate
    /// </summary>
    public float CurrentTime => isActive ? (Time.time - animationStartStamp) : animationStopStamp - animationStartStamp;
    /// <summary>
    /// This property returns an accurate representation of the current time in the animation. Much slower, determined by size of waypoint list
    /// </summary>
    public float AccurateCurrentTime //Congrats to me for making this so small and effecient
    {
        get
        {
            float time = (IsReverse && !isLoop) ? (TotalTime - points[points.Count - 1].TotalPointTime) : 0; //
            int index = IsReverse ? points.Count : -1;
            System.Action f;
            if (isLoop) f = () => time += IsReverse ? points[(index + 1) % points.Count].waitTime + points[index].time : points[index].TotalPointTime;
            else f = () => time += IsReverse ? points[index].waitTime + points[index - 1].time : points[index].TotalPointTime;
            int i = isLoop ? NextIndex : Index;
            while (IsReverse ? (--index > i) : (++index < Index)) f();
            return time + TimeSinceLastWP;
        }
    }
    /// <summary>
    /// Use this property to set isLoop safely. Returns if isLoop can be set. Have not tested. Will look into making this work regardless, somehow.
    /// </summary>
    public bool SetIsLoop
    {
        get => !IsReverse; 
        set => isLoop = IsReverse ? isLoop : value;
    }
    ///<summary>The move time for the current interpolation. Considers if in reverse or not.</summary>
    public float MoveTime => IsReverse ? Next.time : Current.time;
    #region Miscellaneous
    [RuntimeInitializeOnLoadMethod]
    void Add() => SceneManager.sceneLoaded += SetStartTimeToNow;
    static public void SetStartTimeToNow(Scene s, LoadSceneMode l) { levelStartTime = Time.time; }
    /// <summary>
    /// The struct used by WayPath to determine
    /// </summary>
    [System.Serializable]
    public struct Waypoint 
    {
        /// <summary>
        /// The kind of curve the Waypoint has. To learn more about Bezier curves, take a look at https://en.wikipedia.org/wiki/B%C3%A9zier_curve
        /// </summary>
        public enum CurveType
        {
            /// <summary>
            /// This kind of curve only contains the start point and uses the following point to move along a straight line.
            /// </summary>
            Linear,
            /// <summary>
            /// This kind of curve contains the start point and a single tangent that the path curves towards while moving towards the following point
            /// </summary>
            Quadratic,
            /// <summary>
            /// This kind of curve contains the start point and two tangents that the path curves towards while moving towards the following point
            /// </summary>
            Cubic
        }
        public Vector3[] Vectors { get => vectors; }
        [SerializeField]
        Vector3[] vectors;
        /// <summary>
        /// The kind of curve the way point represents
        /// </summary>
        public CurveType Type { get => (CurveType)(vectors.Length - 1); //ADD METHOD TO CHANGE THIS, BUT ALSO RETURN REASONABLE VECTORS WHEN INCREASING DEGREE
            set {
                int k = (int)value + 1;
                Vector3[] n = new Vector3[k];
                int min = Mathf.Min(n.Length, vectors.Length);
                for (int i = 0; i < n.Length; i++)
                    if (i < min) n[i] = vectors[i];
                    else n[i] = vectors[0] + Random.onUnitSphere; 
                vectors = n;
            } }
        /// <summary>
        /// The position of the waypoint.
        /// </summary>
        public Vector3 point { get => vectors[0]; set => vectors[0] = value; }
        /// <summary>
        /// The start tangent of the curve, or the tangent whether Quadratic or Cubic. Vector.zero otherwise.
        /// </summary>
        public Vector3 startTangent { get { if (Type == CurveType.Linear) return Vector3.zero; return vectors[1]; } set { if (Type == CurveType.Linear) return; vectors[1] = value; } }
        /// <summary>
        /// The end tangent of the curve if Cubic. Vector.zero otherwise (PLANNED TO MAKE EQUAL TO END TANGENT OF )
        /// </summary>
        public Vector3 endTangent { get { if (Type != CurveType.Cubic) return Vector3.zero; return vectors[2]; } set { if (Type != CurveType.Cubic) return; vectors[2] = value; } }
        [Min(0.01f)]
        public float time;
        /// <summary>
        /// Time to wait at this point before moving on
        /// </summary>
        [Min(0f)]
        public float waitTime;
        /// <summary>
        /// Used to determine if using linear or smoothstep when interpolating thru point
        /// </summary>
        public bool useLinear;
#if UNITY_EDITOR
        /// <summary>
        /// Is the point folded in the Inspector? DO NOT USE THIS VARIABLE (I mean you can, but you have to exclude it using compiler directives because it is only compiled when in the Unity editor)
        /// </summary>
        public bool foldedInInspector;
#endif 
        public float TotalPointTime { get { return time + waitTime; } }
        public static implicit operator Vector3(Waypoint w) => w.point;
        public Waypoint(Vector3[] vectors, float timeAtPoint, float waitTime, bool useLinear)
        {
            if(vectors.Length < 1 || vectors.Length > 3)
                throw new System.ArgumentOutOfRangeException($"Waypoints constructor given invalid number of vectors. Accepted amounts 1~3. Given {vectors.Length}");
            this.vectors = vectors; 
            time = timeAtPoint;
            this.waitTime = waitTime;
            this.useLinear = useLinear;
#if UNITY_EDITOR
            foldedInInspector = false;
#endif
        }
    }
    /// <summary>
    /// Gets point relative entire hiearchy and additive, aka transforms the point
    /// </summary>
    public Vector3 Relative(Vector3 pt) => transform.parent != null ? transform.parent.TransformPoint(pt + Additive) : (pt + Additive);
    /// <summary>
    /// Gets the world point from a point relative to the hiearchy and additive
    /// </summary>
    public Vector3 Irrelative(Vector3 pt) => (transform.parent != null ? transform.parent.InverseTransformPoint(pt) : pt) - Additive;
    
    #endregion
    public struct MaskOverride
    {

        ///<summary>The layer in question</summary>
        public LayerMask Layer;

        public float Influence;
    }
}
