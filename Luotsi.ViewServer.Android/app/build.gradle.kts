plugins {
    id("com.android.application")
}

val luotsiReleaseKeystorePath = System.getenv("LUOTSI_ANDROID_KEYSTORE_PATH")
val luotsiReleaseKeystorePassword = System.getenv("LUOTSI_ANDROID_KEYSTORE_PASSWORD")
val luotsiReleaseKeyAlias = System.getenv("LUOTSI_ANDROID_KEY_ALIAS")
val luotsiReleaseKeyPassword = System.getenv("LUOTSI_ANDROID_KEY_PASSWORD")
val hasLuotsiReleaseSigning = listOf(
    luotsiReleaseKeystorePath,
    luotsiReleaseKeystorePassword,
    luotsiReleaseKeyAlias,
    luotsiReleaseKeyPassword
).all { !it.isNullOrBlank() }

android {
    namespace = "dev.luotsi.view"
    compileSdk = 35

    defaultConfig {
        applicationId = "dev.luotsi.view"
        minSdk = 21
        targetSdk = 35
        versionCode = 1
        versionName = "0.1.0"
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    signingConfigs {
        if (hasLuotsiReleaseSigning) {
            create("luotsiRelease") {
                storeFile = file(luotsiReleaseKeystorePath!!)
                storePassword = luotsiReleaseKeystorePassword
                keyAlias = luotsiReleaseKeyAlias
                keyPassword = luotsiReleaseKeyPassword
            }
        }
    }

    buildTypes {
        getByName("release") {
            isMinifyEnabled = false
            signingConfig = signingConfigs.getByName(if (hasLuotsiReleaseSigning) "luotsiRelease" else "debug")
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro"
            )
        }
    }
}
