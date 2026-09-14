# Security

Please do not post raw Bluetooth captures, private certificates, device IDs, or personal application paths in public issues. Report a suspected vulnerability privately to the repository maintainer through an available private channel; a dedicated reporting address has not yet been established.

The UIAccess helper can inject input into elevated windows. It validates its sibling engine and session and constrains targets, but is not a security boundary against code already executing as the same user or an administrator. Production distributions require trusted code signing and a protected installation directory. Standard builds have no UIAccess privilege. No third-party audit has been performed.
