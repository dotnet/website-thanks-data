using System.Collections.Generic;

namespace dotnetthanks
{

    public class Release
    {
        public int Id { get; set; }
        public string Name { get; set; }

        public string Tag { get; set;}

        public string TargetCommit { get; set;}

        public List<ChildRepo> ChildRepos { get; set; } = new List<ChildRepo>();

        public List<Contributor> Contributors { get; set; } = new List<Contributor>(); 

        public int Contributions { get; set; }

    }
    
    public class Contributor
    {
        public string Name { get; set; }
        public string Link { get; set; }
        public int Count { get; set; }

        public List<RepoItem> Repos { get; set; } = new List<RepoItem>(); 
    }

    public class RepoItem
    {
        public string Name { get; set; }
        public int Count { get; set; }
    }

    public class ChildRepo
    {
        public string Name { get; set; }
        public string Url { get; set; }

        public string Repository {get => this.Url?.Split("/")[4];}

        public string Owner 
        {
            get => this.Url?.Split("/")[3];
            
        }

        public string Tag { get => Url?.Substring(Url.LastIndexOf($"/") + 1);}
    }
}