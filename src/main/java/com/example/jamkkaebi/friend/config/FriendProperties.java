package com.example.jamkkaebi.friend.config;

import org.springframework.boot.context.properties.ConfigurationProperties;

import java.time.Duration;

/**
 * 친구 시스템 수치. 기본값은 기획서 13장의 숫자이며, 보상 정성만 재화 기획 확정 전의 임시값이다.
 *
 * @param friendLimit                 최대 친구 수
 * @param receivedRequestLimit        받은 요청 대기 상한
 * @param sentRequestLimit            보낸 요청 대기 상한
 * @param requestTtl                  요청 자동 만료 기간
 * @param recommendationCount         하루 추천 인원
 * @param recommendationActiveWindow  추천 후보가 되려면 이 기간 안에 접속했어야 한다
 * @param recommendationCooldownDays  한 번 추천한 사람을 다시 추천하지 않는 일수
 * @param recommendationPoolSize      친구 수가 적은 순으로 추린 뒤 무작위로 고를 후보 수
 * @param lookupPerMinute             친구 코드 조회 분당 한도
 * @param lookupPerDay                친구 코드 조회 하루 한도
 * @param visitRewardJeongseong       친구 전시관 방문 보상 정성 (하루 1회)
 * @param giftExtraConversionLimit    받는 쪽이 하루에 정성으로 환산받을 수 있는 초과 선물 수
 */
@ConfigurationProperties(prefix = "app.friend")
public record FriendProperties(
        Integer friendLimit,
        Integer receivedRequestLimit,
        Integer sentRequestLimit,
        Duration requestTtl,
        Integer recommendationCount,
        Duration recommendationActiveWindow,
        Integer recommendationCooldownDays,
        Integer recommendationPoolSize,
        Integer lookupPerMinute,
        Integer lookupPerDay,
        Integer visitRewardJeongseong,
        Integer giftExtraConversionLimit
) {

    public FriendProperties {
        friendLimit = orDefault(friendLimit, 30);
        receivedRequestLimit = orDefault(receivedRequestLimit, 20);
        sentRequestLimit = orDefault(sentRequestLimit, 10);
        requestTtl = requestTtl == null ? Duration.ofDays(14) : requestTtl;
        recommendationCount = orDefault(recommendationCount, 3);
        recommendationActiveWindow = recommendationActiveWindow == null
                ? Duration.ofDays(7) : recommendationActiveWindow;
        recommendationCooldownDays = orDefault(recommendationCooldownDays, 7);
        recommendationPoolSize = orDefault(recommendationPoolSize, 20);
        lookupPerMinute = orDefault(lookupPerMinute, 10);
        lookupPerDay = orDefault(lookupPerDay, 100);
        visitRewardJeongseong = orDefault(visitRewardJeongseong, 5);
        giftExtraConversionLimit = orDefault(giftExtraConversionLimit, 3);
    }

    /**
     * 상자 선물 발신 보상이자 초과 선물 환산 정성. 기획서가 "(방문 정성) − 2"로 묶어 두었으므로 따로
     * 설정하지 않고 방문 보상에서 계산한다 — 둘을 따로 두면 한쪽만 조정되는 사고가 난다.
     */
    public int giftRewardJeongseong() {
        return Math.max(0, visitRewardJeongseong - 2);
    }

    /** 받는 쪽이 하루에 받을 수 있는 선물 총수 = 상자권 1 + 환산 상한. */
    public int giftReceiveLimit() {
        return 1 + giftExtraConversionLimit;
    }

    private static Integer orDefault(Integer value, int defaultValue) {
        return value == null ? defaultValue : value;
    }
}
