namespace AgentMind.Api.Constants;


public static class AppConstants
{
    /* Keys for configuration lookup to avoid typos */
    public static class ConfigKeys
    {
        public const string CollectionName = "VectorDbConfig:CollectionName";
        public const string VectorSize = "VectorDbConfig:VectorSize";
        public const string SearchLimit = "VectorDbConfig:SearchLimit";
        public const string Categories = "RagConfig:Categories";
        public const string SupportedExtensions = "IngestionConfig:SupportedExtensions";
        public const string LocalPath = "IngestionConfig:LocalKnowledgePath";
        public const string IngestionRoles = "IngestionConfig:Roles";
        public const string DefaultRole = "IngestionConfig:DefaultRole";
    }

    public static class Defaults
    {
        public const string CollectionName = "agentmind_knowledge_base";
        public const int VectorSize = 384;
        public const int SearchLimit = 20;
        public const string Extensions = ".cs,.cpp,.java,.py,.md";
    }
    public static class AgentRoles
    {
        public const string General = "General";
        public const string Developer = "Developer";
        public const string TeamLead = "TeamLead";
        public const string Architect = "Architect";
        public const string TechLead = "TechLead";
        public const string RnDManager = "R&DManager";
        public const string ProductManager = "ProductManager";
        public const string ProjectManager = "ProjectManager";
        public const string QA = "QA";
        public const string Infrastructure = "Infrastructure";
        public const string DefaultRole = "General";
    }
}