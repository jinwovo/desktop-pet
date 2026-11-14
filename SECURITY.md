# Security policy

DesktopPet reads local completion receipts, imports character PNGs and can update the current user's assistant hook definitions. It does not require administrator access or upload conversations.

Report vulnerabilities affecting the current source or latest published release through [GitHub private vulnerability reporting](https://github.com/jinwovo/desktop-pet/security/advisories/new), if the repository has enabled it. If that channel is unavailable, open an issue containing only a request for a private contact method. Do not post tokens, real assistant configurations, conversation contents or an exploit containing personal data publicly. No response deadline or security audit is promised.

Useful report details include affected version, Windows version, expected boundary, minimal reproduction with synthetic files, and observed impact. Areas of interest include character path traversal, reparse points, image decoding budgets, hook configuration preservation, notification receipt handling and install/uninstall manifests.

Download binaries only from releases you trust, compare the supplied SHA256, and keep the complete extracted directory. SHA256 detects content changes; it does not replace publisher authentication. An unsigned build may be flagged by Windows. DesktopPet does not ask you to disable Windows security or approve an assistant hook automatically.

The repository's CI checks are regression checks. Their presence does not establish that a remote workflow has run successfully or that the program has undergone a security audit.
