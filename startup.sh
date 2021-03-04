!#/bin/bash

#token: Github deployment token
#email: Github user email address
#user: Githhub user full name

#dir: working directory
#source: repo to clone

# ensure the git deploy token is used for commits
git config --global url."https://$token:@github.com/".insteadOf "https://github.com/"

# clone the source repository
git clone $source /app/$dir

# bug? must re-init the repo
# set the git user/email for commits
cd /app/$dir && \
  git init && \
  git config --global user.email "$email" && \
  git config --global user.name "$user"

# run the process for creating new entry
dotnet /app/dotnetthanks-loader.dll

# push and create PR
