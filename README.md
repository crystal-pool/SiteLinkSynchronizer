# SiteLinkSynchronizer

A MediaWiki bot that synchronizes sitelinks in the Wikibase repository manually, by looking into the public log events on the client site, especially, the sites powered by WikibaseClientLite.

This bot looks into the recent page move/delete logs, and makes changes to the sitelinks associated entities on the Wikibase repository site, respectively. This bot is designed fix the missing sitelink tracking functionality for the Wikibase "client" sites that actually does not connected to the Wikibase repository directly, and thus cannot notify the repository site about such changes.

## Usage

*   `git clone` the repository.
*   Check the `config.json`, especially if you are not working on [Crystal Pool](https://crystalpool.cxuesong.com).
    *   Create `config._private.json`, if necessary. It will be merged with `config.json` at runtime.
*   Change current directory to the project directory.
*   `dotnet run`

## Example `config._private.json`

Put your password here. This file should be ignored by git.

```json
{
  "WikiSites": {
    "crystalpool": {
      "UserName": "user name here",
      "Password": "password here"
    }
  }
}
```

