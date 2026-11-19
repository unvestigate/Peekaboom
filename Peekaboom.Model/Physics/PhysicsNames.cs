namespace Peekaboom.Model.Physics;

public static class PhysicsNames
{
    public const string RIGID_STATIC = "PxRigidStatic";
    public const string RIGID_DYNAMIC = "PxRigidDynamic";
    public const string RIGID_BODY = "PxRigidBody";
    public const string ARTICULATION_LINK = "PxArticulationLink";

    public const string BOX_GEOMETRY = "PxBoxGeometry";
    public const string SPHERE_GEOMETRY = "PxSphereGeometry";
    public const string CAPSULE_GEOMETRY = "PxCapsuleGeometry";
    public const string PLANE_GEOMETRY = "PxPlaneGeometry";
    public const string CONVEX_GEOMETRY = "PxConvexMeshGeometry";
    public const string TRIANGLE_GEOMETRY = "PxTriangleMeshGeometry";
    public const string HEIGHTFIELD_GEOMETRY = "PxHeightFieldGeometry";
    public const string TETRAHEDRON_GEOMETRY = "PxTetrahedronMeshGeometry";
    public const string CUSTOM_GEOMETRY = "PxCustomGeometry";

    public const string RIGID_STATICS = "RigidStatics";
    public const string RIGID_DYNAMICS = "RigidDynamics";
    public const string ARTICULATIONS = "Articulations";
    public const string LINKS = "Links";
    public const string SHAPES = "Shapes";
    public const string GLOBAL_POSE = "GlobalPose";
    public const string LOCAL_POSE = "LocalPose";
    public const string GEOMETRY = "Geometry";
    public const string SHAPE_FLAGS = "Flags";
    public const string IS_SLEEPING = "IsSleeping";
    public const string RIGID_BODY_FLAGS = "RigidBodyFlags";
    public const string KINEMATIC_FLAG = "eKINEMATIC";
    public const string SIMULATION_FLAG = "eSIMULATION_SHAPE";
    public const string QUERY_FLAG = "eSCENE_QUERY_SHAPE";
    public const string TRIGGER_FLAG = "eTRIGGER_SHAPE";
    public const string NAME = "Name";
    public const string CMASS_LOCAL_POSE = "CMassLocalPose";
    public const string LINEAR_VELOCITY = "LinearVelocity";
    public const string ANGULAR_VELOCITY = "AngularVelocity";

    public const string CONTACTS = "Contacts";
    public const string CONTACT_POINT = "Point";
    public const string CONTACT_AXIS = "Axis";
    public const string CONTACT_SHAPE0 = "Shapes[0]";
    public const string CONTACT_SHAPE1 = "Shapes[1]";
    public const string CONTACT_SEPARATION = "Separation";

    public const string JOINTS = "Joints";
    public const string JOINT_ACTOR0 = "Actors.actor0";
    public const string JOINT_ACTOR1 = "Actors.actor1";
    public const string JOINT_LOCAL_POSE0 = "LocalPose[eACTOR0]";
    public const string JOINT_LOCAL_POSE1 = "LocalPose[eACTOR1]";

    public const string SCENE_QUERIES = "SceneQueries.";
    public const string BATCHED_QUERIES = "BatchedQueries.";
    public const string QUERY_RAYCASTS = "Raycasts";
    public const string QUERY_SWEEPS = "Sweeps";
    public const string QUERY_OVERLAPS = "Overlaps";
    public const string QUERY_HITS = "Hits";
    public const string QUERY_POSES = "PoseList";
    public const string QUERY_ORIGIN = "origin";
    public const string QUERY_DIRECTION = "unitDir";
    public const string QUERY_DISTANCE = "distance";
    public const string QUERY_POSE = "pose";
    public const string QUERY_HITS_BASE = "hits_baseIndex";
    public const string QUERY_HITS_COUNT = "hits_count";
    public const string QUERY_POSE_BASE = "pose_baseIndex";
    public const string HIT_SHAPE = "Shape";
    public const string HIT_ACTOR = "Actor";
    public const string HIT_IMPACT = "Impact";
    public const string HIT_NORMAL = "Normal";

    public const string HALF_EXTENTS = "HalfExtents";
    public const string RADIUS = "Radius";
    public const string HALF_HEIGHT = "HalfHeight";
    public const string MESH_SCALE = "Scale.Scale";
    public const string MESH_SCALE_ROTATION = "Scale.Rotation";
    public const string CONVEX_MESH = "ConvexMesh";
    public const string TRIANGLE_MESH = "TriangleMesh";
    public const string HEIGHTFIELD = "HeightField";
    public const string TETRAHEDRON_MESH = "TetrahedronMesh";
    public const string HEIGHT_SCALE = "HeightScale";
    public const string ROW_SCALE = "RowScale";
    public const string COLUMN_SCALE = "ColumnScale";

    public const string POINTS = "Points";
    public const string VERTICES = "Vertices";
    public const string HULL_POLYGONS = "HullPolygons";
    public const string POLYGON_INDEXES = "PolygonIndexes";
    public const string NUM_VERTICES = "NumVertices";
    public const string INDEX_BASE = "IndexBase";
    public const string TRIANGLES = "Triangles";
    public const string TETRAHEDRONS = "Tetrahedrons";
    public const string NB_ROWS = "NbRows";
    public const string NB_COLUMNS = "NbColumns";
    public const string SAMPLES = "Samples";
    public const string SAMPLE_HEIGHT = "Height";
    public const string SAMPLE_MATERIAL0 = "MaterialIndex[0]";
    public const string SAMPLE_MATERIAL1 = "MaterialIndex[1]";

    public const int HEIGHTFIELD_HOLE = 127;
}
