package com.example.jamkkaebi.purification.service;

/**
 * 관리 플레이 한 판 성공이 복구 순서를 얼마나 줄이는가.
 */
public enum RestoreEffect {
    REFRESH_ONLY, // 복구 없이 선명도 회복만 (선명할 때만)
    ONE_STEP,     // 남은 순서에서 한 페이즈
    FULL          // 한 판에 선명까지
}
