package com.example.jamkkaebi.purification.domain;

// 실패 사유
public enum FailReason {
    DAMAGE_FULL,     // 손상 게이지 100%
    TAPS_EXHAUSTED,  // 탭 예산 소진
    GAVE_UP          // 중도 포기
}
