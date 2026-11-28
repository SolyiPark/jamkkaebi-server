package com.example.jamkkaebi.purification.service;

/**
 * 관리 플레이 한 판 성공이 선명도를 얼마나 올리는가.
 */
public enum RestoreEffect {
    REFRESH_ONLY, // 선명 유지, 타이머만 새로 시작 (선명할 때만)
    ONE_STEP,     // 선명도 한 칸
    FULL          // 한 판에 선명까지
}
