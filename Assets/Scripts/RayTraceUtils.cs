using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.UnityUtils;
using OpenCVForUnity.UtilsModule;
using OpenCVForUnity.ImgprocModule;

public static class RayTraceUtils
{
    public const int MAX_DEPTH = 10;
    public static Dictionary<int, Object> _objects = new Dictionary<int, Object>();
    public static Dictionary<int, LightSource> _lightObjects = new Dictionary<int, LightSource>();
    public static Dictionary<int, VPL> _VPLs = new Dictionary<int, VPL>();

    public static List<Vector2> HaltonSequence(int size)
    {
        List<Vector2> haltonList = new List<Vector2>();
        for (int i = 0; i < size; i++)
        {
            haltonList.Add(new Vector2(Halton(i, 2), Halton(i, 3)));
        }
        return haltonList;
    }

    public static float Halton(int index, float hBase)
    {
        float f = 1;
        float r = 0;
        while (index > 0)
        {
            f /= hBase;
            r += f * (index % hBase);
            index = (int)(index / hBase);
        }
        return r;
    }


    private static Vector3[] SampleLightPoint(LightSource lightObClass)
    {
        Vector3[] points = null;
        Vector3 lightPos = lightObClass.transform.position;
        switch (lightObClass._lightType)
        {
            case LightType.Point:
                points = new Vector3[1];
                points[0] = lightObClass.transform.position;
                break;

            case LightType.Area:
                int sample = lightObClass._nSamples;
                points = new Vector3[sample * sample];

                Vector3 leftTopPos = new Vector3(lightPos.x - 0.5f * lightObClass._area[0], lightPos.y - 0.5f * lightObClass._area[1], lightPos.z);
                Vector2 aspectRatio = new Vector2(lightObClass._area[0] / sample, lightObClass._area[1] / sample);
                for (int w = 0; w < sample; w++)
                {
                    for (int h = 0; h < sample; h++)
                    {
                        points[w * sample + h] = new Vector3(
                            leftTopPos.x + aspectRatio.x * (w + 0.5f), leftTopPos.y + aspectRatio.y * (h + 0.5f), lightPos.z);
                    }
                }
                break;
        }
        return points;
    }

    public static bool IsInShadow(Vector3 lightPos, ref RaycastHit hit)
    {
        Vector3 lv = hit.point - lightPos;
        if (Vector3.Dot(-lv, hit.normal) < 0)
            return false;

        Ray shadowRay = new Ray(lightPos, lv);

        RaycastHit shadowHit;
        if (Physics.Raycast(shadowRay, out shadowHit, lv.magnitude * 0.9999f))
        {
            return true;
        }
        else
        {
            return false;
        }
    }
    public static float GetAOWeight(Ray ray, int AOSamples)
    {
        RaycastHit hit;
        bool isHit = Physics.Raycast(ray, out hit, 100);
        if (isHit)
        {
            float ambientWeight = 0.0f;
            for (int i=0; i<AOSamples;i++)
            {
                Ray rayAmbient = new Ray(hit.point,new Vector3(hit.normal.x + Random.Range(-0.5f, 0.5f),hit.normal.y+Random.Range(0.0f, 0.5f),hit.normal.z+Random.Range(-0.5f, 0.5f)));
                RaycastHit hitAmbient;
                bool isHitAmbient = Physics.Raycast(rayAmbient, out hitAmbient, 3);
                if(!isHitAmbient)
                {
                    ambientWeight += 1.0f / (float)AOSamples;
                }
                else
                {
                    ambientWeight += 0.5f / (float)AOSamples;
                }
            }
            return ambientWeight;
        }
        return 0.0f;
    }

    public static Vector2 PointOnBounds(Bounds bounds, Vector2 aDirection)
    {
        aDirection.Normalize();
        var e = bounds.extents;
        var v = aDirection;
        float y = e.x * v.y / v.x;
        Vector2 res = new Vector2(e.x, y);
        if (Mathf.Abs(y) < e.y)
        {
            if (aDirection.x < 0 && res.x > 0 || aDirection.x > 0 && res.x < 0)
                res.x = -res.x;
            if (aDirection.y < 0 && res.y > 0 || aDirection.y > 0 && res.y < 0)
                res.y = -res.y;
            return res;
        }
        res = new Vector2(e.y * v.x / v.y, e.y);
        if (aDirection.x < 0 && res.x > 0 || aDirection.x > 0 && res.x < 0)
            res.x = -res.x;
        if (aDirection.y < 0 && res.y > 0 || aDirection.y > 0 && res.y < 0)
            res.y = -res.y;
        return res;
    }

    public static float PointOnBoundsLen(int boundWidth, int boundHeight, Vector2 center, Vector2 aDirection)
    {
        Vector2 upLine = new Vector2(-boundWidth / 2.0f, boundHeight);
        return 0f;
    }
}

public static class Voronoi
{
    public static int width = 250;
    public static int height = 250;
    public static Mat _voronoiDiagram;
    public static OpenCVForUnity.CoreModule.Rect _rect;
    public static Subdiv2D _subdiv2D;
    public static List<Vector2> _points;
    public static int _sampleCount = 32;
    public static List<Vector3> _points2Cast;
    private static int _iterCount = 0;
    public enum LightType
    {
        SPOT, POINT, AREA
    }
    public static LightType _lightType = LightType.SPOT;

    public static void Init()
    {
        _iterCount = 0;
        _points2Cast = new List<Vector3>();
        _voronoiDiagram = new Mat(height, width, CvType.CV_8U);
        _rect = new OpenCVForUnity.CoreModule.Rect(0, 0, width, height);
        _subdiv2D = new Subdiv2D(_rect);
    }

    public static void SetPointFromHalton(int size)
    {
        List<Vector2> haltonList = RayTraceUtils.HaltonSequence(size);
        for (int i = 0; i < size; i++)
        {
            _subdiv2D.insert(new Point(haltonList[i].x * width, haltonList[i].y * height));
        }
        _points = haltonList;
    }

    public static void Draw()
    {
        _voronoiDiagram.setTo(Scalar.all(0));
        DrawVoronoi();
        //DrawDelaunay();
        DrawPoints();
    }

    public static void DrawDelaunay()
    {
        MatOfFloat6 triangleMatList = new MatOfFloat6();
        _subdiv2D.getTriangleList(triangleMatList);
        float[] pointArray = triangleMatList.toArray();

        for (int i = 0; i < pointArray.Length / 6; i++)
        {

            Point p0 = new Point(pointArray[i * 6 + 0], pointArray[i * 6 + 1]);
            Point p1 = new Point(pointArray[i * 6 + 2], pointArray[i * 6 + 3]);
            Point p2 = new Point(pointArray[i * 6 + 4], pointArray[i * 6 + 5]);

            if (!(p0.x < 0 || p0.y < 0 || p0.x > width || p0.y > height ||
                p1.x < 0 || p1.y < 0 || p1.x > width || p1.y > height ||
                p2.x < 0 || p2.y < 0 || p2.x > width || p2.y > height))
            {
                Imgproc.line(_voronoiDiagram, p0, p1, new Scalar(10), 1, Imgproc.LINE_AA, 0);
                Imgproc.line(_voronoiDiagram, p1, p2, new Scalar(10), 1, Imgproc.LINE_AA, 0);
                Imgproc.line(_voronoiDiagram, p2, p0, new Scalar(10), 1, Imgproc.LINE_AA, 0);
            }

        }
    }

    public static void DrawPoints()
    {
        for (int i = 0; i < _points.Count; i++)
        {
            Imgproc.circle(_voronoiDiagram, new Point(_points[i].x * width, _points[i].y * height), 3, new Scalar(255), -1, 8, 0);
        }
    }

    public static void DrawPoints(List<Vector2> points)
    {
        for (int i = 0; i < points.Count; i++)
        {
            Imgproc.circle(_voronoiDiagram, new Point(points[i].x * width, points[i].y * height), 5, new Scalar(100), -1, 8, 0);
        }
    }
    
    public static void DrawVoronoi()
    {
        List<MatOfPoint2f> facets = new List<MatOfPoint2f>();
        MatOfPoint2f centPoints = new MatOfPoint2f();
        _subdiv2D.getVoronoiFacetList(new MatOfInt(), facets, centPoints);

        List<MatOfPoint> ifacets = new List<MatOfPoint>();
        ifacets.Add(new MatOfPoint());
        
        for (int i = 0; i < facets.Count; i++)
        {
            
            MatOfPoint ifacet = new MatOfPoint();
            ifacet.fromArray(facets[i].toArray());
            Scalar color = new Scalar(i * 3 % 255);
            Imgproc.fillConvexPoly(_voronoiDiagram, ifacet, color);
            ifacets[0] = ifacet;
            Imgproc.polylines(_voronoiDiagram, ifacets, true, new Scalar(255), 1, Imgproc.LINE_AA, 0);
        }
    }

    public static void DrawEdgePnts()
    {
        MatOfFloat4 edgePnts = new MatOfFloat4();
        _subdiv2D.getEdgeList(edgePnts);
        float[] pointArray = edgePnts.toArray();

        for (int i = 0; i < pointArray.Length / 4; i++)
        {

            Point p0 = new Point(pointArray[i * 4 + 0], pointArray[i * 4 + 1]);
            Point p1 = new Point(pointArray[i * 4 + 2], pointArray[i * 4 + 3]);

            if (!(p0.x < 0 || p0.y < 0 || p0.x > width || p0.y > height ||
                p1.x < 0 || p1.y < 0 || p1.x > width || p1.y > height))
            {
                Imgproc.line(_voronoiDiagram, p0, p1, new Scalar(200), 2, Imgproc.LINE_AA, 0);
            }

        }
    }

    public static List<Vector3> WarpVoronois(List<Vector2> point2Cast)
    {
        List<Vector3> vecs = new List<Vector3>();
        switch (_lightType)
        {
            case LightType.SPOT:
                {
                    for (int i = 0; i < point2Cast.Count; i++)
                    {
                        // Warp 2D Vector to 3d sphere
                        Vector2 pnt = new Vector2(point2Cast[i].x - 0.5f, point2Cast[i].y -0.5f);
                        float len = RayTraceUtils.PointOnBounds(new Bounds(Vector3.zero, Vector3.one), pnt).magnitude;
                        float r = pnt.magnitude;
                        float cos = pnt.x / r;
                        float sin = pnt.y / r;
                        Vector3 resVec = new Vector3(cos, sin, 0.0f);
                        float angle = (1.0f - r / len) * 90.0f; // 0~90
                        Vector2 pntVert = -Vector2.Perpendicular(pnt);
                        resVec = Quaternion.AngleAxis(angle, new Vector3(pntVert.x, pntVert.y, 0)) * resVec;
                        vecs.Add(resVec);
                    }
                }
                break;
            case LightType.POINT:
                {
                    // warp 2d points to 3d sphere points
                    for (int i = 0; i < point2Cast.Count; i++)
                    {
                        // Warp 2D Vector to 3d sphere
                        Vector2 pnt = new Vector2(point2Cast[i].x - 0.5f, point2Cast[i].y - 0.5f);
                        float len = RayTraceUtils.PointOnBounds(new Bounds(Vector3.zero, Vector3.one), pnt).magnitude;
                        float r = pnt.magnitude;
                        float cos = pnt.x / r;
                        float sin = pnt.y / r;
                        Vector3 resVec = new Vector3(cos, sin, 0.0f);
                        float angle = (1.0f - r / len) * 180 - 90f; // -90 ~ 90
                        Vector2 pntVert = -Vector2.Perpendicular(pnt);
                        resVec = Quaternion.AngleAxis(angle, new Vector3(pntVert.x, pntVert.y, 0)) * resVec;
                        vecs.Add(resVec);
                    }
                }
                break;
            case LightType.AREA:
                {

                }
                break;
        }
        return vecs;
    }

    public static void MakeNewVoronois()
    {
        _subdiv2D.Dispose();
        _subdiv2D = new Subdiv2D(_rect);
    }

    private static int FindPointIndexFromList(List<Vector2> points, Vector2 point)
    {
        for (int i = 0; i < points.Count; i++)
        {
            if (points[i].x == point.x && points[i].y == point.y)
            {
                return i;
            }
        }
        return -1;
    }

    private static int FindWorstPoint()
    {
        List<float> pntDistances = new List<float>();
        List<int> pntEdgeCount = new List<int>();
        for (int i = 0; i < _points.Count; i++)
        {
           pntDistances.Add(0f);
            pntEdgeCount.Add(0);
        }
        
        MatOfFloat4 edgePnts = new MatOfFloat4();
        _subdiv2D.getEdgeList(edgePnts);
        float[] pointArray = edgePnts.toArray();

        for (int i = 0; i < pointArray.Length / 4; i++)
        {
            Vector2 p0 = new Vector2(pointArray[i * 4 + 0] / (float)width, pointArray[i * 4 + 1] / (float)height);
            Vector2 p1 = new Vector2(pointArray[i * 4 + 2] / (float)width, pointArray[i * 4 + 3] / (float)height);
            if (p0.x < 0 || p0.y < 0 || p0.x > width || p0.y > height ||
                p1.x < 0 || p1.y < 0 || p1.x > width || p1.y > height)
                continue;
            float distance = Vector2.Distance(p0, p1);
            int p0Idx = FindPointIndexFromList(_points, p0);
            if (p0Idx != -1)
            {
                pntDistances[p0Idx] += distance;
                pntEdgeCount[p0Idx]++;
            }
            int p1Idx = FindPointIndexFromList(_points, p1);
            if (p1Idx != -1)
            {
                pntDistances[p1Idx] += distance;
                pntEdgeCount[p1Idx]++;
            }
        }
        float leastDistance = 100f;
        int leastDisIdx = 0;
        for (int i = 0; i < pntDistances.Count; i++)
        {
            pntDistances[i] /= (float)pntEdgeCount[i];
            if (pntDistances[i] < leastDistance)
            {
                leastDistance = pntDistances[i];
                leastDisIdx = i;
            }
        }
        return leastDisIdx;

    }

    private static void FindBestPoint()
    {
        /*
         Ugly as hell, but it works.
        */
        List<MatOfPoint2f> facets = new List<MatOfPoint2f>();
        MatOfPoint2f centPoints = new MatOfPoint2f();
        _subdiv2D.getVoronoiFacetList(new MatOfInt(), facets, centPoints);

        SortedList<float, int> sortedfacetsDistances = new SortedList<float, int>();
        List<MatOfPoint> ifacets = new List<MatOfPoint>();
        //SortedDictionary
        List<List<Point>> facetsPnts = new List<List<Point>>();
        List<SortedList<float, int>> sortedfacetDistancesList = new List<SortedList<float, int>>();

        for (int i = 0; i < facets.Count; i++)
        {
            SortedList<float, int> sortedfacetDistances = new SortedList<float, int>(); //在第 i 個 facet 的 point 之平均距離
            List<Point> facetsPoints = facets[i].toList();
            facetsPnts.Add(facetsPoints);
            for (int k = 0; k < facetsPoints.Count; k++)
            {
                /*if (facetsPoints[k].x == 0 || facetsPoints[k].y == 0 || facetsPoints[k].x == width || facetsPoints[k].y == height)
                    continue;*/
                List<float> distances = new List<float>();
                SortedList<float, int> sortedDistances = new SortedList<float, int>();
                // 每個 facets 內的點與各個 _point 算距離
                for (int j = 0; j < _points.Count; j++)
                {
                    try
                    {
                        Vector2 pntOnFacet = new Vector2((float)(facetsPoints[k].x / (float)width) - 0.5f, (float)(facetsPoints[k].y / (float)height) - 0.5f);
                        if (pntOnFacet.x < -0.5f || pntOnFacet.y < -0.5f || pntOnFacet.x > 0.5f || pntOnFacet.y > 0.5f)
                        {
                            pntOnFacet = pntOnFacet.normalized * 0.5f;
                            pntOnFacet = RayTraceUtils.PointOnBounds(new Bounds(new Vector3(0.5f, 0.5f, 0.5f), new Vector3(1, 1, 1)), pntOnFacet);
                        }
                        sortedDistances.Add(Vector2.Distance(_points[j], new Vector2(pntOnFacet.x + 0.5f, pntOnFacet.y + 0.5f)), j);
                    }
                    catch (System.ArgumentException)
                    {
                        continue;
                    }
                }
                float avDis = 0;
                for (int j = 0; j < 3; j++)
                {
                    Debug.Log("Dis sort:" + j + ", " + sortedDistances.Keys[j]);
                    avDis += sortedDistances.Keys[j] / 3.0f;
                }
                bool isInserts = true;
                do
                {
                    try
                    {
                        if (isInserts)
                            sortedfacetDistances.Add(avDis, k);
                        else
                        {
                            sortedfacetDistances.Add(avDis + Random.Range(0, 0.1f), k);
                            isInserts = true;
                        }
                    }
                    catch (System.ArgumentException)
                    {
                        isInserts = false;
                    }
                } while (!isInserts);
            }
            sortedfacetDistancesList.Add(sortedfacetDistances);

            bool isInsert = true;
            int idx = sortedfacetDistances.Count - 1;
            do
            {
                try
                {
                    sortedfacetsDistances.Add(sortedfacetDistances.Keys[idx--], i);
                    isInsert = true;
                }
                catch (System.ArgumentException)
                {
                    isInsert = false;
                }
            } while (!isInsert && idx >= 0);
        }

        List<Vector2> points2Cast = new List<Vector2>();

        for (int i = 0; i < Voronoi._sampleCount - _points.Count; i++)
        {
            int idx = sortedfacetsDistances.Count - 1;
            int facetsIdx = sortedfacetsDistances.Values[idx];
            int pointIdx = sortedfacetDistancesList[facetsIdx].Values[sortedfacetDistancesList[facetsIdx].Count - 1];
            Vector2 newPnt = new Vector2((float)facetsPnts[facetsIdx][pointIdx].x, (float)facetsPnts[facetsIdx][pointIdx].y);
            while(PntIsInList(newPnt, _points))
            {
                idx -= 1;
                facetsIdx = sortedfacetsDistances.Values[idx];
                pointIdx = sortedfacetDistancesList[facetsIdx].Values[sortedfacetDistancesList[facetsIdx].Count - 1];
                newPnt.x = (float)facetsPnts[facetsIdx][pointIdx].x;
                newPnt.y = (float)facetsPnts[facetsIdx][pointIdx].y;
            }
            _points.Add(newPnt);
            points2Cast.Add(newPnt);
        }
        _points2Cast = WarpVoronois(points2Cast);
    }
    
    private static bool PntIsInList(Vector2 pnt, List<Vector2> pnts)
    {
        for (int i = 0; i < pnts.Count; i++)
        {
            if (pnts[i] == pnt)
                return true;
        }
        return false;
    }

    public static List<int> UpdatePoints(List<Vector2> points)
    {
        _points.Clear();
        _points.AddRange(points);
        //MakeNewVoronois();
        _subdiv2D.initDelaunay(_rect);
        for (int i = 0; i < _points.Count; i++)
        {
            _subdiv2D.insert(new Point(_points[i].x * width, _points[i].y * height));
        }
        List<int> deletePntIdxs = new List<int>();
        if (++_iterCount > 3)
        {
            deletePntIdxs.Add(FindWorstPoint());
            _iterCount = 0;
        }
        FindBestPoint();
        return deletePntIdxs;
    }
}