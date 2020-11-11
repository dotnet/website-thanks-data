// Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse); 
using System;
using System.Collections.Generic;

    public class Committer    {
        public string name { get; set; } 
        public string email { get; set; } 
        public DateTime date { get; set; } 
    }

    public class Tree    {
        public string sha { get; set; } 
        public string url { get; set; } 
    }

    public class Verification    {
        public bool verified { get; set; } 
        public string reason { get; set; } 
        public object signature { get; set; } 
        public object payload { get; set; } 
    }

    public class Commit    {
        public Author author { get; set; } 
        public Committer committer { get; set; } 
        public string message { get; set; } 
        public Tree tree { get; set; } 
        public string url { get; set; } 
        public int comment_count { get; set; } 
        public Verification verification { get; set; } 
    }

    public class Parent    {
        public string sha { get; set; } 
        public string url { get; set; } 
        public string html_url { get; set; } 
    }

    public class BaseCommit    {
        public string sha { get; set; } 
        public string node_id { get; set; } 
        public Commit commit { get; set; } 
        public string url { get; set; } 
        public string html_url { get; set; } 
        public string comments_url { get; set; } 
        public Author author { get; set; } 
        public Committer committer { get; set; } 
        public List<Parent> parents { get; set; } 
    }

    public class Tree2    {
        public string sha { get; set; } 
        public string url { get; set; } 
    }

    public class Verification2    {
        public bool verified { get; set; } 
        public string reason { get; set; } 
        public object signature { get; set; } 
        public object payload { get; set; } 
    }

    public class Commit2    {
        public Author author { get; set; } 
        public Committer committer { get; set; } 
        public string message { get; set; } 
        public Tree2 tree { get; set; } 
        public string url { get; set; } 
        public int comment_count { get; set; } 
        public Verification2 verification { get; set; } 
    }

    public class Author    {

        public string name { get; set; } 
        public string email { get; set; } 
        public DateTime date { get; set; } 

        public string login { get; set; } 
        public int id { get; set; } 
        public string node_id { get; set; } 
        public string avatar_url { get; set; } 
        public string gravatar_id { get; set; } 
        public string url { get; set; } 
        public string html_url { get; set; } 
        public string followers_url { get; set; } 
        public string following_url { get; set; } 
        public string gists_url { get; set; } 
        public string starred_url { get; set; } 
        public string subscriptions_url { get; set; } 
        public string organizations_url { get; set; } 
        public string repos_url { get; set; } 
        public string events_url { get; set; } 
        public string received_events_url { get; set; } 
        public string type { get; set; } 
        public bool site_admin { get; set; } 
    }

    public class Committer3    {
        public string login { get; set; } 
        public int id { get; set; } 
        public string node_id { get; set; } 
        public string avatar_url { get; set; } 
        public string gravatar_id { get; set; } 
        public string url { get; set; } 
        public string html_url { get; set; } 
        public string followers_url { get; set; } 
        public string following_url { get; set; } 
        public string gists_url { get; set; } 
        public string starred_url { get; set; } 
        public string subscriptions_url { get; set; } 
        public string organizations_url { get; set; } 
        public string repos_url { get; set; } 
        public string events_url { get; set; } 
        public string received_events_url { get; set; } 
        public string type { get; set; } 
        public bool site_admin { get; set; } 
    }

    public class Parent2    {
        public string sha { get; set; } 
        public string url { get; set; } 
        public string html_url { get; set; } 
    }

    public class MergeBaseCommit    {
        public string sha { get; set; } 
        public string node_id { get; set; } 
        public Commit2 commit { get; set; } 
        public string url { get; set; } 
        public string html_url { get; set; } 
        public string comments_url { get; set; } 
        public Author author { get; set; } 
        public Committer3 committer { get; set; } 
        public List<Parent2> parents { get; set; } 
    }

    public class Root    {
        public string url { get; set; } 
        public string html_url { get; set; } 
        public string permalink_url { get; set; } 
        public string diff_url { get; set; } 
        public string patch_url { get; set; } 
        public BaseCommit base_commit { get; set; } 
        public MergeBaseCommit merge_base_commit { get; set; } 
        public string status { get; set; } 
        public int ahead_by { get; set; } 
        public int behind_by { get; set; } 
        public int total_commits { get; set; } 
        public List<MergeBaseCommit> commits { get; set; } 
        public List<object> files { get; set; } 
    }

