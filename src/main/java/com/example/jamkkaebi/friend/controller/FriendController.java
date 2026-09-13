package com.example.jamkkaebi.friend.controller;

import com.example.jamkkaebi.friend.dto.response.FriendExhibitionResponse;
import com.example.jamkkaebi.friend.dto.response.FriendListResponse;
import com.example.jamkkaebi.friend.dto.response.FriendLookupResponse;
import com.example.jamkkaebi.friend.dto.response.GiftResponse;
import com.example.jamkkaebi.friend.dto.response.RecommendationsResponse;
import com.example.jamkkaebi.friend.dto.response.VisitRewardResponse;
import com.example.jamkkaebi.friend.service.FriendRecommendationService;
import com.example.jamkkaebi.friend.service.FriendService;
import com.example.jamkkaebi.friend.service.FriendVisitService;
import com.example.jamkkaebi.friend.service.GiftService;
import com.example.jamkkaebi.global.response.ApiResponse;
import com.example.jamkkaebi.global.security.AuthPrincipal;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.web.bind.annotation.DeleteMapping;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

/**
 * 친구 목록 · 코드 조회 · 추천 · 삭제 · 전시관 방문 · 상자 선물.
 *
 * <p>상대는 언제나 친구 코드로 가리킨다. 내부 사용자 id 는 나가지 않는다.
 */
@RestController
@RequestMapping("/api/friends")
public class FriendController {

    private final FriendService friendService;
    private final FriendRecommendationService recommendationService;
    private final FriendVisitService visitService;
    private final GiftService giftService;

    public FriendController(FriendService friendService,
                            FriendRecommendationService recommendationService,
                            FriendVisitService visitService,
                            GiftService giftService) {
        this.friendService = friendService;
        this.recommendationService = recommendationService;
        this.visitService = visitService;
        this.giftService = giftService;
    }

    @GetMapping
    public ResponseEntity<ApiResponse<FriendListResponse>> getFriends(
            @AuthenticationPrincipal AuthPrincipal principal
    ) {
        return ResponseEntity.ok(ApiResponse.success(
                "OK", "친구 목록을 조회했습니다.", friendService.getFriends(principal.friendCode())));
    }

    @GetMapping("/lookup/{friendCode}")
    public ResponseEntity<ApiResponse<FriendLookupResponse>> lookup(
            @AuthenticationPrincipal AuthPrincipal principal,
            @PathVariable String friendCode
    ) {
        return ResponseEntity.ok(ApiResponse.success(
                "OK", "친구 코드를 조회했습니다.", friendService.lookup(principal.friendCode(), friendCode)));
    }

    @GetMapping("/recommendations")
    public ResponseEntity<ApiResponse<RecommendationsResponse>> getRecommendations(
            @AuthenticationPrincipal AuthPrincipal principal
    ) {
        return ResponseEntity.ok(ApiResponse.success(
                "OK", "오늘의 추천 친구를 조회했습니다.", recommendationService.today(principal.friendCode())));
    }

    @DeleteMapping("/{friendCode}")
    public ResponseEntity<ApiResponse<Void>> removeFriend(
            @AuthenticationPrincipal AuthPrincipal principal,
            @PathVariable String friendCode
    ) {
        friendService.removeFriend(principal.friendCode(), friendCode);
        return ResponseEntity.ok(ApiResponse.success("FRIEND_REMOVED", "친구를 삭제했습니다.", null));
    }

    @GetMapping("/{friendCode}/exhibition")
    public ResponseEntity<ApiResponse<FriendExhibitionResponse>> getExhibition(
            @AuthenticationPrincipal AuthPrincipal principal,
            @PathVariable String friendCode
    ) {
        return ResponseEntity.ok(ApiResponse.success(
                "OK", "친구 전시관을 열람했습니다.", visitService.exhibition(principal.friendCode(), friendCode)));
    }

    //방문 보상 받기 API
    @PostMapping("/{friendCode}/visit-reward")
    public ResponseEntity<ApiResponse<VisitRewardResponse>> claimVisitReward(
            @AuthenticationPrincipal AuthPrincipal principal,
            @PathVariable String friendCode
    ) {
        return ResponseEntity.ok(ApiResponse.success(
                "VISIT_RECORDED", "방문을 기록했습니다.", visitService.claimReward(principal.friendCode(), friendCode)));
    }

    @PostMapping("/{friendCode}/gift")
    public ResponseEntity<ApiResponse<GiftResponse>> sendGift(
            @AuthenticationPrincipal AuthPrincipal principal,
            @PathVariable String friendCode
    ) {
        return ResponseEntity.ok(ApiResponse.success(
                "GIFT_SENT", "상자를 선물했습니다.", giftService.send(principal.friendCode(), friendCode)));
    }
}
