## ADDED Requirements

### Requirement: Continuous integration workflow

The repository SHALL contain a GitHub Actions workflow at `.github/workflows/ci.yml` that runs on pull requests targeting `main` and on pushes to `main`. It SHALL restore, build the solution treating warnings as errors, run the tests, validate `/docs/index.json` against the content tree, and compile the Bicep templates. The workflow SHALL fail if any of these steps fails, and it SHALL NOT require access to Azure.

#### Scenario: A sound pull request passes

- **WHEN** a pull request builds cleanly, its tests pass, its catalog is consistent, and its Bicep compiles
- **THEN** the CI workflow concludes successfully

#### Scenario: A failing test blocks the pull request

- **WHEN** a pull request introduces a failing unit test
- **THEN** the CI workflow fails and reports the failing test

#### Scenario: An inconsistent catalog blocks the pull request

- **WHEN** a pull request adds a document under `/docs` without a catalog entry
- **THEN** the CI workflow fails at the catalog validation step

#### Scenario: A broken template blocks the pull request

- **WHEN** a pull request introduces a Bicep syntax error
- **THEN** the CI workflow fails at the template compilation step

#### Scenario: CI needs no cloud credentials

- **WHEN** the CI workflow runs on a fork's pull request
- **THEN** it completes without requesting Azure credentials or repository secrets

### Requirement: Continuous deployment workflow

The repository SHALL contain a GitHub Actions workflow at `.github/workflows/cd.yml` that runs on pushes to `main` affecting `src/**`, `docs/**`, `infra/**`, or the workflow file itself, and that can also be started manually. It SHALL deploy the infrastructure, build and push the container image, then redeploy the infrastructure pinned to that image, in that order.

#### Scenario: A code change is deployed

- **WHEN** a commit changing `src/**` is pushed to `main`
- **THEN** the workflow builds and pushes an image and the container app ends up running it

#### Scenario: A content change is deployed

- **WHEN** a commit changing only `docs/**` is pushed to `main`
- **THEN** the workflow runs and the deployed app serves the updated content

#### Scenario: An unrelated change does not deploy

- **WHEN** a commit changing only `README.md` is pushed to `main`
- **THEN** the CD workflow does not run

#### Scenario: Manual deployment

- **WHEN** a maintainer starts the workflow manually
- **THEN** it deploys the current state of `main`

#### Scenario: First run into an empty resource group

- **WHEN** the workflow runs against a resource group with no prior deployment
- **THEN** the first infrastructure deployment creates the registry, the image push succeeds against it, and the second deployment pins the app to the new image

### Requirement: Azure authentication uses federated credentials

The deployment workflow SHALL authenticate to Azure using OpenID Connect federated credentials, requesting an `id-token: write` permission. No Azure client secret, registry password, or other long-lived credential SHALL be stored in the repository or its secrets. The target subscription, resource group, and registry SHALL be supplied as GitHub Environment configuration rather than hard-coded in the workflow.

#### Scenario: Deployment authenticates without a stored secret

- **WHEN** the CD workflow runs
- **THEN** it obtains an Azure token via OIDC and no client secret is read

#### Scenario: No long-lived credentials in the repository

- **WHEN** the workflow files are inspected
- **THEN** no client secret, registry password, or connection string is present, in plaintext or as a secret reference

#### Scenario: Environment configuration drives the target

- **WHEN** the environment's subscription or resource group value changes
- **THEN** the next deployment targets the new value with no workflow edit

### Requirement: Images are tagged by commit

Every image pushed by the deployment workflow SHALL be tagged with the full commit SHA of the deployed commit, and SHALL additionally be tagged `latest`. The infrastructure deployment SHALL reference the image by its commit SHA tag, never by `latest`.

#### Scenario: A deployment is traceable to a commit

- **WHEN** a deployment completes
- **THEN** the registry holds an image tagged with that commit's SHA and the container app's image reference names that SHA

#### Scenario: Rollback uses an existing tag

- **WHEN** a maintainer redeploys the infrastructure with a previous commit's SHA tag
- **THEN** the app runs that image without rebuilding it

### Requirement: Deployment reports its result

The deployment workflow SHALL surface the deployed application's endpoint in the workflow run summary, and SHALL fail the run if the deployed application does not report healthy after deployment.

#### Scenario: A successful deployment publishes the endpoint

- **WHEN** the workflow completes successfully
- **THEN** the run summary contains the container app's HTTPS endpoint

#### Scenario: An unhealthy deployment fails the run

- **WHEN** the deployed application's health endpoint does not report healthy after deployment
- **THEN** the workflow run fails
