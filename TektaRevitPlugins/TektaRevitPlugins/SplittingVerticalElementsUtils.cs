using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using RevitAppServices = Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using RevitExceptions=Autodesk.Revit.Exceptions;
using RevitCreation = Autodesk.Revit.Creation;



namespace TektaRevitPlugins
{
    static class SplittingVerticalElementsUtils
    {
        #region Static Data Fields
        static Document document;
        static ElementId selectedElemId;
        static Element selectedElem;
        static Solid selectedElemSolid;
        #endregion

        public static void Split(Document doc, ElementId elemId)
        {
            document = doc;
            selectedElemId = elemId;
            selectedElem = document.GetElement(elemId);
            selectedElemSolid = GetUppermostSolid(selectedElem);

            try
            {
                // Acquire interfering elements
                IList<Element> interferingElems
                    = GetInterferingElems(selectedElem);

                if (interferingElems.Count == 0)
                    return;

                if (selectedElem is Wall)
                {
                    IList<ElementId> inserts =
                        (selectedElem as Wall).FindInserts(true, true, true, true);

                    if (inserts.Count != 0)
                    {
                        return;
                    }

                    if (ModifiedProfile((Wall)selectedElem))
                        return;



                    // Get hold of all solids involed in the clash
                    IList<Solid> interferingSolids =
                        GetInterferingElemsAsSolids(interferingElems);

                    // Perform the boolean opeartions to get the resulting solid
                    Solid resultingSolid =
                        GetResultingSolid(selectedElemSolid, interferingSolids);

                    // Find the face whose normal matches the wall's normal
                    Face face = null;
                    XYZ wallOrientation = ((Wall)selectedElem).Orientation;
                    foreach (Face currFace in resultingSolid.Faces)
                    {
                        XYZ faceNormal = currFace
                            .ComputeNormal(new UV(0, 0)).Normalize();

                        if (Math.Round(
                            wallOrientation.DotProduct(faceNormal), 2) > 0.1)
                        {
                            face = currFace;
                            break;
                        }
                    }

                    if (face == null)
                        throw new ArgumentNullException("Face is null");

                    // Get a set of curveloops from the face
                    IList<CurveLoop> crvLoops = face.GetEdgesAsCurveLoops();

                    IList<CurveLoop> orderedCrvloops =
                        (crvLoops.OrderBy(crvloop =>
                        {
                            Curve crv = crvloop
                                .Where(c => GetDirectionVector(c).Z == 1)
                                .First();

                            return crv.GetEndPoint(0).Z;
                        })).ToList();

                    // Get Wall's properties
                    Wall wall = (Wall)selectedElem;
                    WallType wallType = wall.WallType;

                    using (Transaction t = new Transaction(doc, "Create walls"))
                    {
                        t.Start();
                        for (int i = 0; i < orderedCrvloops.Count; i++)
                        {
                            // Select a curve
                            Curve selectedCrv =
                                orderedCrvloops[i].Where(crv =>
                                                         GetDirectionVector(crv).Z == 1).First();

                            double currWallHeight = selectedCrv.ApproximateLength;

                            if (i == 0)
                            {
                                double offset = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET).AsDouble();

                                Wall.Create(doc, ((LocationCurve)wall.Location).Curve, wallType.Id, wall.LevelId,
                                            currWallHeight, offset, false, true);
                            }
                            else
                            {
                                Element intruder = interferingElems
                                    .ElementAt(i - 1);

                                ElementId currLevelId = intruder.LevelId;

                                double offset = intruder
                                    .get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)
                                    .AsDouble();

                                Wall.Create(doc, ((LocationCurve)wall.Location).Curve, wallType.Id, currLevelId,
                                            currWallHeight, offset, false, true);
                            }
                        }
                        doc.Delete(wall.Id);
                        t.Commit();
                    }

                }
                else
                {
                    List<Curve> crvs = new List<Curve>();
                    IDictionary<Curve, Level> crvsLvls = new Dictionary<Curve, Level>();

                    Curve currCrv = GetColumnAxis(selectedElem);

                    ElementId prevElemId =
                        interferingElems.ElementAt(0).LevelId;

                    for (int i = 0; i < interferingElems.Count; i++)
                    {
                        Element currElem = interferingElems.ElementAt(i);

                        Solid elemSolid = GetUppermostSolid(currElem);

                        SolidCurveIntersection results =
                            elemSolid.IntersectWithCurve
                            (currCrv,
                             new SolidCurveIntersectionOptions
                             { ResultType = SolidCurveIntersectionMode.CurveSegmentsOutside });

                        if (results.SegmentCount == 2)
                        {
                            // if it is not the last segment
                            if (i != interferingElems.Count - 1)
                            {
                                crvs.Add(results.GetCurveSegment(0));
                                currCrv = results.GetCurveSegment(1);
                                crvsLvls.Add(results.GetCurveSegment(0), (Level)doc.GetElement(prevElemId));
                            }
                            else
                            {
                                crvs.Add(results.GetCurveSegment(0));
                                crvs.Add(results.GetCurveSegment(1));
                                crvsLvls.Add(results.GetCurveSegment(0), (Level)doc.GetElement(prevElemId));
                                crvsLvls.Add(results.GetCurveSegment(1), (Level)doc.GetElement(currElem.LevelId));
                            }
                        }
                        else
                        {
                            currCrv = results.GetCurveSegment(0);
                        }
                        prevElemId = currElem.LevelId;
                    }

                    FamilySymbol columnType = ((FamilyInstance)selectedElem).Symbol;

                    using (Transaction t = new Transaction(doc, "Split Column"))
                    {
                        t.Start();
                        foreach (Curve crv in crvsLvls.Keys)
                        {
                            doc.Create.NewFamilyInstance
                                (crv, columnType, crvsLvls[crv], StructuralType.Column);
                        }

                        doc.Delete(selectedElem.Id);
                        t.Commit();

                    }
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Exception", string.Format("StackTrace:\n{0}\nMessage:\n{1}",
                                                          ex.StackTrace, ex.Message));
            }
        }

        static IList<Element> GetInterferingElems(Element elem)
        {
            Solid solid = GetUppermostSolid(elem);
            if (solid == null)
                throw new ArgumentNullException("GetUppermostSolid(elem)==null");
            FilteredElementCollector collector =
                new FilteredElementCollector(document);
            ElementIntersectsSolidFilter intrudingElemFilter =
                new ElementIntersectsSolidFilter(solid, false);
            ExclusionFilter exclusionFilter =
                new ExclusionFilter(new List<ElementId> { elem.Id });
            ICollection<Element> envadingElem = collector
                //.OfClass(typeof(Floor))
                .WherePasses(exclusionFilter)
                .WherePasses(intrudingElemFilter)
                .WhereElementIsNotElementType()
                .ToElements();

            IList<Element> orderedEnvadingElemIds =
                envadingElem.OrderBy((Element e) =>
                {
                    return ((Level)document
                            .GetElement(e.LevelId)).Elevation;
                }).ToList();

            return orderedEnvadingElemIds;
        }

        static Solid GetUppermostSolid(Element elem)
        {
            Options opts = new Options { IncludeNonVisibleObjects = true };

            foreach (GeometryObject gObjL1 in elem.get_Geometry(new Options()))
            {
                if (gObjL1 is GeometryInstance)
                {
                    GeometryInstance instance =
                        gObjL1 as GeometryInstance;

                    foreach (GeometryObject gObjL2 in
                            instance.GetInstanceGeometry(instance.Transform))
                    {
                        if (gObjL2 is Solid)
                        {
                            return (Solid)gObjL2;
                        }
                    }
                }

                else if (gObjL1 is Solid)
                {
                    return (Solid)gObjL1;
                }
            }

            return null;
        }

        static Curve GetColumnAxis(Element elem)
        {
            Options options =
                new Options { IncludeNonVisibleObjects = true };

            foreach (GeometryObject gObjL1 in
                    elem.get_Geometry(options))
            {
                if (gObjL1 is Curve)
                    return (Curve)gObjL1;
            }

            return null;
        }

        static IList<Solid> GetInterferingElemsAsSolids(IList<Element> interferingElems)
        {

            return interferingElems.Select(e =>
            {
                return GetUppermostSolid(e);
            }).ToList();
        }

        static Solid GetResultingSolid(Solid solid, IList<Solid> solids)
        {
            Solid resultingSolid = solid;

            foreach (Solid currSolid in solids)
            {
                resultingSolid = BooleanOperationsUtils
                    .ExecuteBooleanOperation(resultingSolid, currSolid,
                                             BooleanOperationsType.Difference);
            }

            return resultingSolid;
        }

        static void GetDirectShape(Document doc, Solid solid)
        {
            using (Transaction t = new Transaction(doc, "Create DirectShape object"))
            {
                t.Start();
                DirectShape ds = DirectShape
                    .CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel),
                                   doc.Application.ActiveAddInId.ToString(),
                                   "Geometry object id");
                ds.SetShape(new GeometryObject[] { solid });
                t.Commit();
            }
        }

        static XYZ GetDirectionVector(Curve curve)
        {
            XYZ direction = null;

            if (curve is Line)
            {
                // Pick up the tangent vector of the wall
                direction = curve.ComputeDerivatives(0, true).BasisX.Normalize();
            }
            else
            {
                direction = (curve.GetEndPoint(1)
                           - curve.GetEndPoint(0)).Normalize();
            }

            return direction;
        }

        static bool ModifiedProfile(Wall wall)
        {
            Solid solid = GetUppermostSolid(wall);
            XYZ orientation = wall.Orientation;
            Face face = null;
            foreach (Face f in solid.Faces)
            {
                if (orientation
                   .DotProduct(f.ComputeNormal(new UV(0, 0))) > 0)
                {
                    face = f;
                    break;
                }
            }
            if (face == null)
                throw new ArgumentNullException("Something has gone wrong!");

            if (face.GetEdgesAsCurveLoops().Count > 1)
                return true;
            else
                return false;
        }
    }
}
