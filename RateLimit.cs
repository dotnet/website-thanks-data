
public class RateLimit
{
    public Resources resources { get; set; }
    public Rate rate { get; set; }
}

public class Resources
{
    public Core core { get; set; }

    public Search search { get; set; }
    public Graphql graphql { get; set; }
    public IntegationManifest integration_manifest { get; set; }

    public SourceImport source_import { get; set; }
    public CodeScanningUpload code_scanning_upload { get; set; }
}

public class CodeScanningUpload : Metrics
{
}

public class SourceImport : Metrics
{
}

public class IntegationManifest : Metrics
{
}

public class Graphql : Metrics
{
}

public class Search : Metrics
{
}

public class Core : Metrics
{
}

public class Rate : Metrics
{
}

public abstract class Metrics
{
    public int limit { get; set; }
    public int used { get; set; }
    public int remaining { get; set; }
    public int reset { get; set; }
}
