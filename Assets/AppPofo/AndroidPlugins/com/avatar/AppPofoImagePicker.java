package com.apppofo.avatar;

import android.app.Activity;
import android.app.Fragment;
import android.app.FragmentManager;
import android.content.Intent;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.provider.MediaStore;

import com.unity3d.player.UnityPlayer;

import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;

// 이 코드는 Android의 시스템 사진 선택 화면을 열고,
// 사용자가 선택한 이미지를 Unity가 읽을 수 있는 임시 파일로 복사한 뒤
// 선택 성공 / 취소 / 실패 결과를 Unity C# 코드로 전달하기 위한 코드입니다.

public final class AppPofoImagePicker
{
    // Fragment를 중복 생성하지 않기 위해 사용하는 고유 이름입니다.
    private static final String FRAGMENT_TAG =
            "AppPofoImagePickerFragment";


    // Unity에서 이 메서드를 호출하면
    // Android 시스템 사진 선택 화면을 실행합니다.    
    public static void pickImage(
            Activity activity,
            String unityGameObjectName)
    {
        if (activity == null)
        {
            sendMessage(
                    unityGameObjectName,
                    "OnAndroidImagePickFailed",
                    "Android Activity를 찾을 수 없습니다.");

            return;
        }


        activity.runOnUiThread(() ->
        {
            try
            {
                FragmentManager fragmentManager =
                        activity.getFragmentManager();


                PickerFragment fragment =
                        (PickerFragment) fragmentManager
                                .findFragmentByTag(FRAGMENT_TAG);


                // 아직 PickerFragment가 없다면 새로 생성합니다.
                if (fragment == null)
                {
                    fragment =
                            new PickerFragment();


                    fragmentManager
                            .beginTransaction()
                            .add(fragment, FRAGMENT_TAG)
                            .commitAllowingStateLoss();


                    fragmentManager
                            .executePendingTransactions();
                }


                fragment.openPicker(
                        unityGameObjectName);
            }
            catch (Exception ex)
            {
                sendMessage(
                        unityGameObjectName,
                        "OnAndroidImagePickFailed",
                        ex.getMessage());
            }
        });
    }


    // Java에서 발생한 결과를
    // Unity GameObject의 C# 메서드로 전달합니다.
    private static void sendMessage(
            String gameObjectName,
            String methodName,
            String message)
    {
        if (gameObjectName == null ||
            gameObjectName.trim().isEmpty())
        {
            return;
        }


        UnityPlayer.UnitySendMessage(
                gameObjectName,
                methodName,
                message == null ? "" : message);
    }


    // 사진 선택 Activity의 결과를 받기 위해 사용하는 Fragment입니다.
    public static class PickerFragment
            extends Fragment
    {
        private static final int REQUEST_PICK_IMAGE =
                41001;

        private static final String TARGET_KEY =
                "AppPofoTargetObject";


        private String unityGameObjectName;


        @Override
        public void onCreate(
                Bundle savedInstanceState)
        {
            super.onCreate(
                    savedInstanceState);


            setRetainInstance(true);


            // Android가 Fragment를 다시 생성한 경우
            // 기존 Unity GameObject 이름을 복원합니다.
            if (savedInstanceState != null)
            {
                unityGameObjectName =
                        savedInstanceState.getString(
                                TARGET_KEY);
            }
        }


        @Override
        public void onSaveInstanceState(
                Bundle outState)
        {
            super.onSaveInstanceState(
                    outState);


            outState.putString(
                    TARGET_KEY,
                    unityGameObjectName);
        }


        // Android 버전에 맞는
        // 시스템 사진 선택 화면을 실행합니다.
        public void openPicker(
                String gameObjectName)
        {
            unityGameObjectName =
                    gameObjectName;


            Intent intent;


            // Android 13 이상에서는
            // 시스템 Photo Picker를 사용합니다.
            if (Build.VERSION.SDK_INT >= 33)
            {
                intent =
                        new Intent(
                                MediaStore.ACTION_PICK_IMAGES);


                intent.setType(
                        "image/*");
            }
            else
            {
                // Android 12 이하에서는
                // 시스템 문서 선택 화면을 사용합니다.
                intent =
                        new Intent(
                                Intent.ACTION_OPEN_DOCUMENT);


                intent.addCategory(
                        Intent.CATEGORY_OPENABLE);


                intent.setType(
                        "image/*");
            }


            try
            {
                startActivityForResult(
                        intent,
                        REQUEST_PICK_IMAGE);
            }
            catch (Exception ex)
            {
                sendMessage(
                        unityGameObjectName,
                        "OnAndroidImagePickFailed",
                        ex.getMessage());
            }
        }


        // 사용자가 사진 선택을 끝냈을 때
        // Android가 이 메서드로 결과를 돌려줍니다.
        @Override
        public void onActivityResult(
                int requestCode,
                int resultCode,
                Intent data)
        {
            super.onActivityResult(
                    requestCode,
                    resultCode,
                    data);


            if (requestCode != REQUEST_PICK_IMAGE)
                return;


            // 사용자가 사진 선택을 취소한 경우입니다.
            if (resultCode != Activity.RESULT_OK)
            {
                sendMessage(
                        unityGameObjectName,
                        "OnAndroidImagePickCancelled",
                        "");

                return;
            }


            if (data == null ||
                data.getData() == null)
            {
                sendMessage(
                        unityGameObjectName,
                        "OnAndroidImagePickFailed",
                        "선택한 이미지 정보를 가져오지 못했습니다.");

                return;
            }


            copySelectedImage(
                    data.getData());
        }


        // Android의 content:// URI 이미지를
        // Unity가 File.ReadAllBytes()로 읽을 수 있는 임시 파일로 복사합니다.
        private void copySelectedImage(
                Uri imageUri)
        {
            Activity activity =
                    getActivity();


            if (activity == null)
            {
                sendMessage(
                        unityGameObjectName,
                        "OnAndroidImagePickFailed",
                        "Android Activity를 찾을 수 없습니다.");

                return;
            }


            try
            {
                File cacheFile =
                        new File(
                                activity.getCacheDir(),
                                "apppofo_avatar_" +
                                System.currentTimeMillis() +
                                ".img");


                try (
                        InputStream inputStream =
                                activity
                                        .getContentResolver()
                                        .openInputStream(imageUri);

                        FileOutputStream outputStream =
                                new FileOutputStream(cacheFile)
                )
                {
                    if (inputStream == null)
                    {
                        throw new Exception(
                                "선택한 이미지 스트림을 열 수 없습니다.");
                    }


                    byte[] buffer =
                            new byte[8192];


                    int read;


                    while ((read =
                            inputStream.read(buffer)) != -1)
                    {
                        outputStream.write(
                                buffer,
                                0,
                                read);
                    }
                }


                // Unity에는 URI가 아니라
                // 실제 임시 파일 경로를 전달합니다.
                sendMessage(
                        unityGameObjectName,
                        "OnAndroidImagePicked",
                        cacheFile.getAbsolutePath());
            }
            catch (Exception ex)
            {
                sendMessage(
                        unityGameObjectName,
                        "OnAndroidImagePickFailed",
                        ex.getMessage());
            }
        }
    }
}