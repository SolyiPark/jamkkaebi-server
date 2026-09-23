package com.example.jamkkaebi.box.config;

import org.springframework.boot.context.properties.ConfigurationProperties;

import java.time.Duration;

/**
 * 일일 상자 수치(임시값)
 *
 * @param workbenchCapacity  작업대에 동시에 올릴 수 있는 미확인 유물 수. 가득 차면 어떤 경로로도
 *                           상자를 열 수 없다 — 상자를 쌓아두지 못하게 막아 정화 플레이로 유도한다
 * @param newCardWeight      미획득 카드 가중치
 * @param duplicateWeight    중복 카드 가중치
 * @param pityThreshold      한 유물이 이 횟수만큼 연속으로 안 나오면 다음 상자에서 확정 등장
 * @param duplicateJeongseong 중복 카드 1장이 주는 정성
 * @param duplicateCrystal   중복 카드 1장이 주는 해당 시대의 결정
 * @param purchasePrice      정성으로 상자를 더 여는 값
 * @param purchaseDailyLimit 1일 구매 상한. {@code null} 이면 상한 없음
 * @param logRetention       상자 기록 보관 기간.
 */
@ConfigurationProperties(prefix = "app.box")
public record BoxProperties(
        Integer workbenchCapacity,
        Integer newCardWeight,
        Integer duplicateWeight,
        Integer pityThreshold,
        Integer duplicateJeongseong,
        Integer duplicateCrystal,
        Integer purchasePrice,
        Integer purchaseDailyLimit,
        Duration logRetention
) {

    public BoxProperties {
        workbenchCapacity = orDefault(workbenchCapacity, 3);
        newCardWeight = orDefault(newCardWeight, 70);
        duplicateWeight = orDefault(duplicateWeight, 30);
        pityThreshold = orDefault(pityThreshold, 7);
        duplicateJeongseong = orDefault(duplicateJeongseong, 10);
        duplicateCrystal = orDefault(duplicateCrystal, 3);
        purchasePrice = orDefault(purchasePrice, 50);
        purchaseDailyLimit = orDefault(purchaseDailyLimit, 3);
        logRetention = logRetention == null ? Duration.ofDays(30) : logRetention;
    }

    // 음수면 구매 불가능
    public boolean purchasable() {
        return purchasePrice >= 0;
    }

    private static Integer orDefault(Integer value, int defaultValue) {
        return value == null ? defaultValue : value;
    }
}
